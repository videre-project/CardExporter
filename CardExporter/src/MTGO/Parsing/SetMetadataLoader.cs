/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using Microsoft.Extensions.Logging;
using MTGOSDK.API.Collection;
using MTGOSDK.Core.Reflection;


namespace CardExporter.MTGO.Parsing;

internal static class SetMetadataLoader
{
  public static IReadOnlyDictionary<string, SetMetadata> Load(string dataDirectory, ILogger logger)
  {
    var metadata = new Dictionary<string, SetMetadata>(StringComparer.OrdinalIgnoreCase);
    var missingCodes = new List<string>();

    foreach (string code in EnumerateSetCodes(dataDirectory))
    {
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

    logger.LogInformation("Loaded runtime metadata for {SetCount} MTGO sets.", metadata.Count);
    int missingNameCount = metadata.Values.Count(static item => string.IsNullOrWhiteSpace(item.Name));
    if (missingNameCount > 0)
    {
      logger.LogWarning("Runtime set names were not available for {MissingNameCount} of {SetCount} MTGO sets.", missingNameCount, metadata.Count);
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

    XmlReaderSettings settings = new()
    {
      DtdProcessing = DtdProcessing.Ignore,
      IgnoreComments = true,
      IgnoreWhitespace = true
    };
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    foreach (string file in Directory.EnumerateFiles(cardDataDirectory, "client_*.xml").Order(StringComparer.OrdinalIgnoreCase))
    {
      if (file.EndsWith("_DO.xml", StringComparison.OrdinalIgnoreCase))
      {
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
}
