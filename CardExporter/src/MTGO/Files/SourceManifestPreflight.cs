/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using MTGOSDK.API;


namespace CardExporter.MTGO.Files;

internal static class SourceManifestPreflight
{
  public const string FileName = "mtgo-source-files.xml";
  public const string ParserOutputVersion = "5";
  public static string OutputVersion => Client.Version.ToString();

  public static SourceManifestComparison CompareCardData(
    string manifestRoot,
    IEnumerable<SourceFile> currentSourceFiles
  )
  {
    return Compare(manifestRoot, currentSourceFiles, SourceManifestScope.CardData);
  }

  public static SourceManifestComparison CompareLegalities(
    string manifestRoot,
    IEnumerable<SourceFile> currentSourceFiles
  )
  {
    return Compare(manifestRoot, currentSourceFiles, SourceManifestScope.Legality);
  }

  public static SourceManifestComparison CompareAssets(
    string manifestRoot,
    IEnumerable<SourceFile> currentSourceFiles
  )
  {
    return Compare(manifestRoot, currentSourceFiles, SourceManifestScope.Asset);
  }

  private static SourceManifestComparison Compare(
    string manifestRoot,
    IEnumerable<SourceFile> currentSourceFiles,
    SourceManifestScope scope
  )
  {
    string manifestPath = Path.Combine(manifestRoot, FileName);
    if (!File.Exists(manifestPath))
    {
      return SourceManifestComparison.Changed($"source manifest was not found at {manifestPath}");
    }

    if (!TryReadManifest(
      manifestPath,
      out string? manifestOutputVersion,
      out string? manifestParserOutputVersion,
      out SourceManifestImportCounts? importCounts,
      out List<SourceManifestFile> allManifestFiles,
      out string? error))
    {
      return SourceManifestComparison.Changed(error ?? "source manifest could not be read");
    }

    if (!string.Equals(manifestOutputVersion, OutputVersion, StringComparison.Ordinal))
    {
      return SourceManifestComparison.Changed(
        $"MTGO client version changed from {manifestOutputVersion ?? "(none)"} to {OutputVersion}"
      );
    }

    if (!string.Equals(manifestParserOutputVersion, ParserOutputVersion, StringComparison.Ordinal))
    {
      return SourceManifestComparison.Changed(
        $"parser output version changed from {manifestParserOutputVersion ?? "(none)"} to {ParserOutputVersion}"
      );
    }

    if (importCounts is null)
    {
      return SourceManifestComparison.Changed("source manifest is missing expected import row counts");
    }

    Dictionary<string, SourceManifestFile> manifestFiles = allManifestFiles
      .Where(file => IsInScope(file, scope))
      .ToDictionary(static file => file.RelativePath, StringComparer.OrdinalIgnoreCase);
    Dictionary<string, SourceManifestFile> currentFiles = currentSourceFiles
      .GroupBy(static file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
      .ToDictionary(
        static group => group.Key,
        group =>
        {
          SourceFile file = group.First();
          return new SourceManifestFile(
            file.RelativePath,
            file.ByteCount,
            file.Sha256,
            GetCurrentSourceType(file.RelativePath, scope)
          );
        },
        StringComparer.OrdinalIgnoreCase
      );

    string? reason = null;

    if (manifestFiles.Count != currentFiles.Count)
    {
      reason = $"source file count changed from {manifestFiles.Count} to {currentFiles.Count}";
    }

    foreach (KeyValuePair<string, SourceManifestFile> item in currentFiles.OrderBy(static item => item.Key, StringComparer.OrdinalIgnoreCase))
    {
      string path = item.Key;
      SourceManifestFile currentFile = item.Value;
      if (!manifestFiles.TryGetValue(path, out SourceManifestFile? manifestFile))
      {
        reason ??= $"source file was added: {path}";
        continue;
      }

      if (manifestFile.ByteCount != currentFile.ByteCount)
      {
        reason ??= $"source file byte count changed for {path}: {manifestFile.ByteCount} -> {currentFile.ByteCount}";
        continue;
      }

      if (!string.Equals(manifestFile.Sha256, currentFile.Sha256, StringComparison.OrdinalIgnoreCase))
      {
        reason ??= $"source file hash changed for {path}";
      }
    }

    foreach (KeyValuePair<string, SourceManifestFile> item in manifestFiles.OrderBy(static item => item.Key, StringComparer.OrdinalIgnoreCase))
    {
      if (currentFiles.ContainsKey(item.Key))
      {
        continue;
      }

      reason ??= $"source file was removed: {item.Key}";
    }

    if (reason is not null)
    {
      return SourceManifestComparison.Changed(reason);
    }

    return SourceManifestComparison.Unchanged(currentFiles.Count, manifestPath, importCounts);
  }

  private static bool TryReadManifest(
    string manifestPath,
    out string? outputVersion,
    out string? parserOutputVersion,
    out SourceManifestImportCounts? importCounts,
    out List<SourceManifestFile> manifestFiles,
    out string? error
  )
  {
    outputVersion = null;
    parserOutputVersion = null;
    importCounts = null;
    manifestFiles = new List<SourceManifestFile>();
    error = null;

    XDocument document;
    try
    {
      document = XDocument.Load(manifestPath);
    }
    catch (XmlException exception)
    {
      error = $"source manifest XML could not be parsed: {manifestPath}: {exception.Message}";
      return false;
    }
    catch (IOException exception)
    {
      error = $"source manifest could not be read: {manifestPath}: {exception.Message}";
      return false;
    }

    if (document.Root is null || !string.Equals(document.Root.Name.LocalName, "sourceManifest", StringComparison.Ordinal))
    {
      error = $"source manifest root element must be sourceManifest: {manifestPath}";
      return false;
    }

    outputVersion = (string?)document.Root.Attribute("outputVersion");
    parserOutputVersion = (string?)document.Root.Attribute("parserOutputVersion");
    importCounts = TryReadImportCounts(document.Root);

    var seenManifestKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    int rowNumber = 0;
    foreach (XElement element in document.Root.Elements("sourceFile"))
    {
      rowNumber++;

      string? sourceType = (string?)element.Attribute("sourceType");
      if (string.IsNullOrWhiteSpace(sourceType))
      {
        error = $"source manifest sourceFile {rowNumber} had an empty sourceType: {manifestPath}";
        return false;
      }

      string normalizedSourceType = sourceType.ToLowerInvariant();
      if (!IsKnownSourceType(normalizedSourceType))
      {
        error = $"source manifest sourceFile {rowNumber} had an invalid sourceType '{sourceType}': {manifestPath}";
        return false;
      }

      string? relativePath = (string?)element.Attribute("relativePath");
      if (string.IsNullOrWhiteSpace(relativePath))
      {
        error = $"source manifest sourceFile {rowNumber} had an empty relativePath: {manifestPath}";
        return false;
      }

      string? byteCountValue = (string?)element.Attribute("byteCount");
      if (!long.TryParse(byteCountValue, NumberStyles.None, CultureInfo.InvariantCulture, out long byteCount))
      {
        error = $"source manifest sourceFile {rowNumber} had an invalid byteCount: {manifestPath}";
        return false;
      }

      string? sha256 = (string?)element.Attribute("sha256");
      if (string.IsNullOrWhiteSpace(sha256))
      {
        error = $"source manifest sourceFile {rowNumber} had an empty sha256: {manifestPath}";
        return false;
      }

      if (!seenManifestKeys.Add(CreateManifestKey(normalizedSourceType, relativePath)))
      {
        error = $"source manifest sourceFile {rowNumber} duplicated {normalizedSourceType} relativePath {relativePath}: {manifestPath}";
        return false;
      }

      manifestFiles.Add(new SourceManifestFile(relativePath, byteCount, sha256, normalizedSourceType));
    }

    return true;
  }

  private static SourceManifestImportCounts? TryReadImportCounts(XElement root)
  {
    if (!TryReadLongAttribute(root, "setCount", out long setCount) ||
        !TryReadLongAttribute(root, "cardCount", out long cardCount) ||
        !TryReadLongAttribute(root, "productCount", out long productCount) ||
        !TryReadLongAttribute(root, "oracleCardCount", out long oracleCardCount) ||
        !TryReadLongAttribute(root, "cardFaceCount", out long cardFaceCount) ||
        !TryReadLongAttribute(root, "legalityCount", out long legalityCount))
    {
      return null;
    }

    return new SourceManifestImportCounts(
      setCount,
      cardCount,
      productCount,
      oracleCardCount,
      cardFaceCount,
      legalityCount
    );
  }

  private static bool TryReadLongAttribute(
    XElement root,
    string attributeName,
    out long value
  )
  {
    value = 0;
    string? rawValue = (string?)root.Attribute(attributeName);
    return !string.IsNullOrWhiteSpace(rawValue) &&
      long.TryParse(rawValue, NumberStyles.None, CultureInfo.InvariantCulture, out value) &&
      value >= 0;
  }

  private static bool IsInScope(SourceManifestFile file, SourceManifestScope scope)
  {
    return scope switch
    {
      SourceManifestScope.CardData =>
        string.Equals(file.SourceType, "shared", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(file.SourceType, "set", StringComparison.OrdinalIgnoreCase),
      SourceManifestScope.Legality =>
        string.Equals(file.SourceType, "legality", StringComparison.OrdinalIgnoreCase),
      SourceManifestScope.Asset =>
        string.Equals(file.SourceType, "asset", StringComparison.OrdinalIgnoreCase),
      _ => false
    };
  }

  private static string GetCurrentSourceType(string relativePath, SourceManifestScope scope)
  {
    return scope switch
    {
      SourceManifestScope.CardData => GetCurrentCardDataSourceType(relativePath),
      SourceManifestScope.Legality => "legality",
      SourceManifestScope.Asset => "asset",
      _ => GetCurrentCardDataSourceType(relativePath)
    };
  }

  private static string GetCurrentCardDataSourceType(string relativePath)
  {
    return GetSetCode(relativePath) is null ? "shared" : "set";
  }

  private static string CreateManifestKey(string sourceType, string relativePath)
  {
    return sourceType + "\0" + relativePath;
  }

  private static bool IsKnownSourceType(string sourceType)
  {
    return string.Equals(sourceType, "shared", StringComparison.Ordinal) ||
      string.Equals(sourceType, "set", StringComparison.Ordinal) ||
      string.Equals(sourceType, "legality", StringComparison.Ordinal) ||
      string.Equals(sourceType, "asset", StringComparison.Ordinal);
  }

  private static string? GetSetCode(string relativePath)
  {
    string fileName = Path.GetFileName(relativePath);
    if (!fileName.StartsWith("client_", StringComparison.OrdinalIgnoreCase) ||
        !fileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
    {
      return null;
    }

    return fileName.EndsWith("_DO.xml", StringComparison.OrdinalIgnoreCase)
      ? fileName["client_".Length..^"_DO.xml".Length]
      : fileName["client_".Length..^".xml".Length];
  }

  private sealed record SourceManifestFile(
    string RelativePath,
    long ByteCount,
    string Sha256,
    string SourceType
  );

  private enum SourceManifestScope
  {
    CardData,
    Legality,
    Asset
  }
}

internal sealed record SourceManifestComparison(
  bool HasChanges,
  string Reason,
  int SourceFileCount,
  string? ManifestPath,
  SourceManifestImportCounts? ImportCounts
)
{
  public static SourceManifestComparison Changed(string reason)
  {
    return new SourceManifestComparison(
      true,
      reason,
      0,
      null,
      null
    );
  }

  public static SourceManifestComparison Unchanged(
    int sourceFileCount,
    string manifestPath,
    SourceManifestImportCounts importCounts
  )
  {
    return new SourceManifestComparison(
      false,
      "source manifest matches current source files",
      sourceFileCount,
      manifestPath,
      importCounts
    );
  }
}

internal sealed record SourceManifestImportCounts(
  long SetCount,
  long CardCount,
  long ProductCount,
  long OracleCardCount,
  long CardFaceCount,
  long LegalityCount
);
