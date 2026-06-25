/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using MTGOSDK.API.Collection;
using MTGOSDK.Core.Reflection;
using MTGOSDK.Win32;


namespace CardExporter.MTGO.Parsing;

internal static class SetMetadataLoader
{
  public static IReadOnlyDictionary<string, SetMetadata> Load(string dataDirectory, ILogger logger)
  {
    var metadata = new Dictionary<string, SetMetadata>(StringComparer.OrdinalIgnoreCase);
    var missingCodes = new List<string>();
    int enumMetadataCount = 0;
    EnumSetMetadataReader? enumSetMetadataReader = EnumSetMetadataReader.TryCreate(logger);

    foreach (string code in EnumerateSetCodes(dataDirectory))
    {
      if (enumSetMetadataReader is not null && enumSetMetadataReader.TryGet(code, out SetMetadata? enumMetadata))
      {
        metadata[code] = enumMetadata;
        enumMetadataCount++;
        continue;
      }

      try
      {
        Set set = CollectionManager.GetSet(code);
        DateOnly? releaseDate = set.ReleaseDate == default
          ? null
          : DateOnly.FromDateTime(set.ReleaseDate);

        metadata[code] = new SetMetadata(
          Name: ResolveSetName(code, set),
          ReleaseDate: releaseDate,
          Age: set.Age > 0 ? set.Age : null,
          SetType: set.Type.ToString()
        );
      }
      catch
      {
        missingCodes.Add(code);
      }
    }

    logger.LogInformation("Loaded set metadata for {SetCount} MTGO sets.", metadata.Count);
    if (enumSetMetadataReader is not null)
    {
      logger.LogInformation("Loaded direct MTGOEnumStruct metadata for {SetCount} MTGO sets.", enumMetadataCount);
    }

    int missingNameCount = metadata.Values.Count(static item => string.IsNullOrWhiteSpace(item.Name));
    if (missingNameCount > 0)
    {
      logger.LogInformation(
        "Direct/runtime set metadata did not include names for {MissingNameCount} of {SetCount} MTGO sets; product-name fallback may fill product-only set rows.",
        missingNameCount,
        metadata.Count
      );
    }

    if (missingCodes.Count > 0)
    {
      logger.LogWarning("Runtime metadata was not available for {MissingSetCount} MTGO sets.", missingCodes.Count);
    }

    return metadata;
  }

  private static IEnumerable<string> EnumerateSetCodes(string dataDirectory)
  {
    string cardDataDirectory = Path.Combine(dataDirectory, "CardDataSource");
    if (!Directory.Exists(cardDataDirectory))
    {
      yield break;
    }

    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    XmlReaderSettings settings = new()
    {
      DtdProcessing = DtdProcessing.Ignore,
      IgnoreComments = true,
      IgnoreWhitespace = true
    };

    foreach (string file in Directory.EnumerateFiles(cardDataDirectory, "client_*.xml").Order(StringComparer.OrdinalIgnoreCase))
    {
      if (file.EndsWith("_DO.xml", StringComparison.OrdinalIgnoreCase))
      {
        if (TryReadProductSetCode(file, out string? productSetCode) && seen.Add(productSetCode))
        {
          yield return productSetCode;
        }

        continue;
      }

      using XmlReader reader = XmlReader.Create(file, settings);
      while (reader.Read())
      {
        if (reader.NodeType != XmlNodeType.Element || reader.Name != "CardSet")
        {
          continue;
        }

        string? code = reader.GetAttribute("id");
        if (!string.IsNullOrWhiteSpace(code) && seen.Add(code))
        {
          yield return code;
        }

        break;
      }
    }

    string lookupPath = Path.Combine(cardDataDirectory, "CARDSETNAME_STRING.xml");
    if (!File.Exists(lookupPath))
    {
      yield break;
    }

    foreach (XElement element in XDocument.Load(lookupPath).Descendants("CARDSETNAME_STRING_ITEM"))
    {
      string code = element.Value;
      if (!string.IsNullOrWhiteSpace(code) && seen.Add(code))
      {
        yield return code;
      }
    }
  }

  private static bool TryReadProductSetCode(string file, out string code)
  {
    string fileName = Path.GetFileName(file);
    const string prefix = "client_";
    const string suffix = "_DO.xml";

    if (!fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
        !fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
    {
      code = string.Empty;
      return false;
    }

    code = fileName[prefix.Length..^suffix.Length];
    return !string.IsNullOrWhiteSpace(code);
  }

  private static string? ResolveSetName(string code, Set set)
  {
    string? name = TryReadCommonSetDescription(set);
    if (name is not null)
    {
      return name;
    }

    name = NullIfWhiteSpace(set.Name);
    return string.Equals(name, code, StringComparison.OrdinalIgnoreCase) ? null : name;
  }

  private static string? TryReadCommonSetDescription(Set set)
  {
    try
    {
      dynamic rawSet = DLRWrapper.Unbind(set);
      dynamic commonSet = rawSet.CommonSet;
      string? name = NullIfWhiteSpace(Convert.ToString(commonSet.Description));
      return string.Equals(name, set.Code, StringComparison.OrdinalIgnoreCase) ? null : name;
    }
    catch
    {
      return null;
    }
  }

  private static string? NullIfWhiteSpace(string? value)
  {
    return string.IsNullOrWhiteSpace(value) ? null : value;
  }

  private sealed class EnumSetMetadataReader
  {
    private const string EnumAssemblyName = "MTGOEnumStruct.dll";
    private const string CardSetTypeName = "WotC.MTGO.Common.CardSet";
    private const string CardSetTypeTypeName = "WotC.MTGO.Common.CardSetType";

    private readonly MethodInfo _getCardSetFromKey;
    private readonly MethodInfo _getCardSetTypeFromKey;

    private EnumSetMetadataReader(MethodInfo getCardSetFromKey, MethodInfo getCardSetTypeFromKey)
    {
      _getCardSetFromKey = getCardSetFromKey;
      _getCardSetTypeFromKey = getCardSetTypeFromKey;
    }

    public static EnumSetMetadataReader? TryCreate(ILogger logger)
    {
      string? appDirectory = Constants.MTGOAppDirectory;
      if (string.IsNullOrWhiteSpace(appDirectory))
      {
        logger.LogWarning("MTGOAppDirectory could not be resolved; falling back to CollectionManager set metadata.");
        return null;
      }

      string assemblyPath = Path.Combine(appDirectory, EnumAssemblyName);
      if (!File.Exists(assemblyPath))
      {
        logger.LogWarning("MTGO enum metadata assembly was not found at {AssemblyPath}; falling back to CollectionManager set metadata.", assemblyPath);
        return null;
      }

      try
      {
        Assembly assembly = Assembly.LoadFile(assemblyPath);
        Type cardSetType = assembly.GetType(CardSetTypeName, throwOnError: true)!;
        Type cardSetTypeType = assembly.GetType(CardSetTypeTypeName, throwOnError: true)!;

        MethodInfo getCardSetFromKey = cardSetType.GetMethod("GetFromKey", BindingFlags.Public | BindingFlags.Static) ??
          throw new MissingMethodException(CardSetTypeName, "GetFromKey");
        MethodInfo getCardSetTypeFromKey = cardSetTypeType.GetMethod("GetFromKey", BindingFlags.Public | BindingFlags.Static) ??
          throw new MissingMethodException(CardSetTypeTypeName, "GetFromKey");

        logger.LogInformation("Loaded MTGO enum metadata from {AssemblyPath}.", assemblyPath);
        return new EnumSetMetadataReader(getCardSetFromKey, getCardSetTypeFromKey);
      }
      catch (Exception ex)
      {
        logger.LogWarning(ex, "MTGO enum metadata could not be loaded; falling back to CollectionManager set metadata.");
        return null;
      }
    }

    public bool TryGet(string code, out SetMetadata metadata)
    {
      metadata = default!;

      try
      {
        object? cardSet = _getCardSetFromKey.Invoke(null, [code]);
        if (cardSet is null)
        {
          return false;
        }

        string? name = ReadString(cardSet, "Description");
        string? setCode = ReadString(cardSet, "CardSetCd");
        string? releaseDateValue = ReadString(cardSet, "ReleaseDate");
        string? setTypeCode = ReadString(cardSet, "CardSetTypeCd");

        metadata = new SetMetadata(
          Name: string.Equals(name, code, StringComparison.OrdinalIgnoreCase) ? null : name,
          ReleaseDate: TryParseDate(releaseDateValue, out DateOnly releaseDate) ? releaseDate : null,
          Age: null,
          SetType: ResolveSetTypeName(setTypeCode)
        );

        return !string.IsNullOrWhiteSpace(setCode);
      }
      catch
      {
        return false;
      }
    }

    private string? ResolveSetTypeName(string? setTypeCode)
    {
      if (string.IsNullOrWhiteSpace(setTypeCode))
      {
        return null;
      }

      object? cardSetType = _getCardSetTypeFromKey.Invoke(null, [setTypeCode]);
      return cardSetType is null
        ? setTypeCode
        : ReadString(cardSetType, "EnumValue") ?? setTypeCode;
    }

    private static bool TryParseDate(string? value, out DateOnly result)
    {
      result = default;
      if (string.IsNullOrWhiteSpace(value))
      {
        return false;
      }

      if (DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out result))
      {
        return true;
      }

      if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dateTime))
      {
        result = DateOnly.FromDateTime(dateTime);
        return true;
      }

      return false;
    }

    private static string? ReadString(object instance, string propertyName)
    {
      PropertyInfo? property = instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
      object? value = property?.GetValue(instance);
      return NullIfWhiteSpace(Convert.ToString(value, CultureInfo.InvariantCulture));
    }
  }
}
