/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using CardExporter.MTGO.Records;
using Microsoft.Extensions.Logging;


namespace CardExporter.MTGO.Files;

internal static class SourceManifestWriter
{
  private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

  public static void Write(
    string manifestRoot,
    Parser parser,
    SourceManifestImportCounts? importCounts,
    ILogger logger
  )
  {
    Directory.CreateDirectory(manifestRoot);

    List<SourceFile> parsedFiles = parser
      .EnumerateParsedSourceFiles()
      .OrderBy(static file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
      .ToList();
    List<SourceFile> sharedFiles = parser
      .EnumerateSharedSourceFiles()
      .OrderBy(static file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
      .ToList();
    List<SourceFile> productFiles = parsedFiles
      .Where(static file => IsProductObjectSourceFile(file.RelativePath))
      .OrderBy(static file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
      .ToList();
    List<SourceFile> legalityFiles = parser
      .EnumerateLegalitySourceFiles()
      .OrderBy(static file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
      .ToList();
    Dictionary<string, SourceFile> sourceFilesByPath = parsedFiles.ToDictionary(
      static file => file.RelativePath,
      StringComparer.OrdinalIgnoreCase
    );
    List<SetRecord> sets = parser
      .EnumerateSets()
      .OrderBy(static set => set.Code, StringComparer.OrdinalIgnoreCase)
      .ToList();

    string manifestPath = Path.Combine(manifestRoot, SourceManifestPreflight.FileName);
    SourceManifestState existingState = ReadExistingManifestState(manifestPath, logger);
    List<XElement> assetRows = existingState.Rows
      .Where(static element =>
        string.Equals((string?)element.Attribute("sourceType"), "asset", StringComparison.OrdinalIgnoreCase))
      .Select(static element => new XElement(element))
      .ToList();
    List<SourceManifestRow> legalityRows = CreateLegalityManifestRows(legalityFiles);
    List<XElement> rows = CreateManifestRows(sourceFilesByPath, sharedFiles, productFiles, sets, logger)
      .Concat(legalityRows)
      .Select(CreateSourceFileElement)
      .Concat(assetRows)
      .ToList();
    rows = SortManifestElements(rows).ToList();
    WriteXmlIfChanged(manifestPath, rows, importCounts);

    logger.LogInformation(
      "Wrote source manifest with {SourceFileCount} card data source files and {LegalitySourceFileCount} legality source files to {ManifestPath}.",
      rows.Count - legalityRows.Count - assetRows.Count,
      legalityRows.Count,
      manifestPath
    );
  }

  public static void WriteLegalities(
    string manifestRoot,
    Parser parser,
    SourceManifestImportCounts? importCounts,
    ILogger logger
  )
  {
    Directory.CreateDirectory(manifestRoot);

    List<SourceFile> legalityFiles = parser
      .EnumerateLegalitySourceFiles()
      .OrderBy(static file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
      .ToList();
    SourceManifestState existingState = ReadExistingManifestState(
      Path.Combine(manifestRoot, SourceManifestPreflight.FileName),
      logger
    );
    IReadOnlyList<XElement> nonLegalityRows = existingState.Rows
      .Where(static element =>
        !string.Equals((string?)element.Attribute("sourceType"), "legality", StringComparison.OrdinalIgnoreCase))
      .ToList();
    if (nonLegalityRows.Count == 0)
    {
      logger.LogWarning(
        "Existing card data source manifest rows were not available; writing a full source manifest instead."
      );
      Write(manifestRoot, parser, importCounts, logger);
      return;
    }

    SourceManifestImportCounts? manifestImportCounts = importCounts ?? existingState.ImportCounts;
    List<XElement> legalityRows = CreateLegalityManifestRows(legalityFiles)
      .Select(CreateSourceFileElement)
      .ToList();
    List<XElement> rows = SortManifestElements(nonLegalityRows.Concat(legalityRows)).ToList();
    string manifestPath = Path.Combine(manifestRoot, SourceManifestPreflight.FileName);
    WriteXmlIfChanged(manifestPath, rows, manifestImportCounts);

    logger.LogInformation(
      "Updated source manifest legality rows with {LegalitySourceFileCount} legality source files in {ManifestPath}.",
      legalityRows.Count,
      manifestPath
    );
  }

  public static void WriteAssets(
    string manifestRoot,
    string appDirectory,
    ILogger logger
  )
  {
    Directory.CreateDirectory(manifestRoot);

    string manifestPath = Path.Combine(manifestRoot, SourceManifestPreflight.FileName);
    SourceManifestState existingState = ReadExistingManifestState(manifestPath, logger);
    List<XElement> nonAssetRows = existingState.Rows
      .Where(static element =>
        !string.Equals((string?)element.Attribute("sourceType"), "asset", StringComparison.OrdinalIgnoreCase))
      .ToList();
    List<XElement> assetRows = AssetSourceFiles.Enumerate(appDirectory)
      .Select(static file => CreateSourceFileElement(new SourceManifestRow(
        SourceType: SourceType.Asset,
        SetCode: null,
        SetName: null,
        SourceFile: file
      )))
      .ToList();

    List<XElement> rows = SortManifestElements(nonAssetRows.Concat(assetRows)).ToList();
    WriteXmlIfChanged(manifestPath, rows, existingState.ImportCounts);

    logger.LogInformation(
      "Updated source manifest asset rows with {AssetSourceFileCount} MTGO application files in {ManifestPath}.",
      assetRows.Count,
      manifestPath
    );
  }

  private static List<SourceManifestRow> CreateManifestRows(
    IReadOnlyDictionary<string, SourceFile> sourceFilesByPath,
    IReadOnlyList<SourceFile> sharedFiles,
    IReadOnlyList<SourceFile> productFiles,
    IReadOnlyList<SetRecord> sets,
    ILogger logger
  )
  {
    List<SourceManifestRow> rows = new List<SourceManifestRow>(sharedFiles.Count + productFiles.Count + sets.Count);
    Dictionary<string, string?> setNamesByCode = sets.ToDictionary(
      static set => set.Code,
      static set => set.Name,
      StringComparer.OrdinalIgnoreCase
    );
    rows.AddRange(sharedFiles.Select(static file => new SourceManifestRow(
      SourceType: SourceType.Shared,
      SetCode: null,
      SetName: null,
      SourceFile: file
    )));

    foreach (SetRecord set in sets)
    {
      if (!sourceFilesByPath.TryGetValue(set.SourceFile, out SourceFile? setSourceFile))
      {
        logger.LogWarning("Skipping source manifest for set {SetCode}; source file {SourceFile} was not indexed.", set.Code, set.SourceFile);
        continue;
      }

      rows.Add(new SourceManifestRow(
        SourceType: SourceType.Set,
        SetCode: set.Code,
        SetName: set.Name,
        SourceFile: setSourceFile
      ));
    }

    foreach (SourceFile productFile in productFiles)
    {
      string? setCode = GetProductObjectSetCode(productFile.RelativePath);
      rows.Add(new SourceManifestRow(
        SourceType: SourceType.Set,
        SetCode: setCode,
        SetName: setCode is not null && setNamesByCode.TryGetValue(setCode, out string? setName) ? setName : null,
        SourceFile: productFile
      ));
    }

    return rows;
  }

  private static bool IsProductObjectSourceFile(string relativePath)
  {
    return Path.GetFileName(relativePath).EndsWith("_DO.xml", StringComparison.OrdinalIgnoreCase);
  }

  private static string? GetProductObjectSetCode(string relativePath)
  {
    string fileName = Path.GetFileName(relativePath);
    if (!fileName.StartsWith("client_", StringComparison.OrdinalIgnoreCase) ||
        !fileName.EndsWith("_DO.xml", StringComparison.OrdinalIgnoreCase))
    {
      return null;
    }

    return fileName["client_".Length..^"_DO.xml".Length];
  }

  private static List<SourceManifestRow> SortManifestRows(IEnumerable<SourceManifestRow> rows)
  {
    return rows
      .OrderBy(static row => row.SourceType)
      .ThenBy(static row => row.SetCode, StringComparer.OrdinalIgnoreCase)
      .ThenBy(static row => row.SourceFile.RelativePath, StringComparer.OrdinalIgnoreCase)
      .ToList();
  }

  private static IEnumerable<XElement> SortManifestElements(IEnumerable<XElement> rows)
  {
    return rows
      .OrderBy(static row => GetSourceTypeSortValue((string?)row.Attribute("sourceType")))
      .ThenBy(static row => (string?)row.Attribute("setCode"), StringComparer.OrdinalIgnoreCase)
      .ThenBy(static row => (string?)row.Attribute("relativePath"), StringComparer.OrdinalIgnoreCase);
  }

  private static int GetSourceTypeSortValue(string? sourceType)
  {
    return sourceType?.ToLowerInvariant() switch
    {
      "shared" => 0,
      "set" => 1,
      "legality" => 2,
      "asset" => 3,
      _ => 4
    };
  }

  private static List<SourceManifestRow> CreateLegalityManifestRows(
    IReadOnlyList<SourceFile> legalityFiles
  )
  {
    return legalityFiles
      .Select(static file => new SourceManifestRow(
        SourceType: SourceType.Legality,
        SetCode: null,
        SetName: null,
        SourceFile: file
      ))
      .OrderBy(static row => row.SourceFile.RelativePath, StringComparer.OrdinalIgnoreCase)
      .ToList();
  }

  private static void WriteXmlIfChanged(
    string path,
    IReadOnlyList<SourceManifestRow> rows,
    SourceManifestImportCounts? importCounts
  )
  {
    WriteXmlIfChanged(path, rows.Select(CreateSourceFileElement).ToList(), importCounts);
  }

  private static void WriteXmlIfChanged(
    string path,
    IReadOnlyList<XElement> rows,
    SourceManifestImportCounts? importCounts
  )
  {
    using var stream = new MemoryStream();
    var settings = new XmlWriterSettings
    {
      Encoding = Utf8NoBom,
      Indent = true,
      NewLineChars = "\n"
    };

    using (XmlWriter writer = XmlWriter.Create(stream, settings))
    {
      CreateXml(rows, importCounts).Save(writer);
    }

    byte[] bytes = stream.ToArray();
    if (File.Exists(path) && File.ReadAllBytes(path).SequenceEqual(bytes))
    {
      return;
    }

    string temporaryPath = path + ".tmp";
    File.WriteAllBytes(temporaryPath, bytes);
    File.Move(temporaryPath, path, overwrite: true);
  }

  private static XDocument CreateXml(
    IReadOnlyList<XElement> rows,
    SourceManifestImportCounts? importCounts
  )
  {
    List<object> content =
    [
      new XAttribute("outputVersion", SourceManifestPreflight.OutputVersion),
      new XAttribute("parserOutputVersion", SourceManifestPreflight.ParserOutputVersion)
    ];

    if (importCounts is not null)
    {
      content.Add(new XAttribute("setCount", importCounts.SetCount.ToString(CultureInfo.InvariantCulture)));
      content.Add(new XAttribute("cardCount", importCounts.CardCount.ToString(CultureInfo.InvariantCulture)));
      content.Add(new XAttribute("productCount", importCounts.ProductCount.ToString(CultureInfo.InvariantCulture)));
      content.Add(new XAttribute("cardCatalogVariantCount", importCounts.CardCatalogVariantCount.ToString(CultureInfo.InvariantCulture)));
      content.Add(new XAttribute("oracleCardCount", importCounts.OracleCardCount.ToString(CultureInfo.InvariantCulture)));
      content.Add(new XAttribute("cardFaceCount", importCounts.CardFaceCount.ToString(CultureInfo.InvariantCulture)));
      content.Add(new XAttribute("legalityCount", importCounts.LegalityCount.ToString(CultureInfo.InvariantCulture)));
    }

    content.AddRange(rows.Select(static row => new XElement(row)));

    return new XDocument(
      new XDeclaration("1.0", "utf-8", null),
      new XElement(
        "sourceManifest",
        content
      )
    );
  }

  private static XElement CreateSourceFileElement(SourceManifestRow row)
  {
    return new XElement(
      "sourceFile",
      new XAttribute("sourceType", row.SourceType.ToString().ToLowerInvariant()),
      string.IsNullOrWhiteSpace(row.SetCode) ? null : new XAttribute("setCode", row.SetCode),
      string.IsNullOrWhiteSpace(row.SetName) ? null : new XAttribute("setName", row.SetName),
      new XAttribute("relativePath", row.SourceFile.RelativePath),
      new XAttribute("byteCount", row.SourceFile.ByteCount.ToString(CultureInfo.InvariantCulture)),
      new XAttribute("sha256", row.SourceFile.Sha256)
    );
  }

  private static SourceManifestState ReadExistingManifestState(string manifestPath, ILogger logger)
  {
    if (!File.Exists(manifestPath))
    {
      return SourceManifestState.Empty;
    }

    try
    {
      XDocument document = XDocument.Load(manifestPath, LoadOptions.None);
      if (document.Root is null ||
          !string.Equals(document.Root.Name.LocalName, "sourceManifest", StringComparison.Ordinal))
      {
        logger.LogWarning("Could not preserve card data source rows because {ManifestPath} is not a source manifest.", manifestPath);
        return SourceManifestState.Empty;
      }

      List<XElement> rows = document.Root
        .Elements("sourceFile")
        .Select(static element => new XElement(element))
        .ToList();

      return new SourceManifestState(
        rows,
        TryReadImportCounts(document.Root)
      );
    }
    catch (Exception exception)
    {
      logger.LogWarning(exception, "Could not read existing source manifest rows from {ManifestPath}.", manifestPath);
      return SourceManifestState.Empty;
    }
  }

  private static SourceManifestImportCounts? TryReadImportCounts(XElement root)
  {
    if (!TryReadLongAttribute(root, "setCount", out long setCount) ||
        !TryReadLongAttribute(root, "cardCount", out long cardCount) ||
        !TryReadLongAttribute(root, "productCount", out long productCount) ||
        !TryReadLongAttribute(root, "cardCatalogVariantCount", out long cardCatalogVariantCount) ||
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
      cardCatalogVariantCount,
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

  private sealed record SourceManifestState(
    IReadOnlyList<XElement> Rows,
    SourceManifestImportCounts? ImportCounts
  )
  {
    public static readonly SourceManifestState Empty = new([], null);
  }

  private sealed record SourceManifestRow(
    SourceType SourceType,
    string? SetCode,
    string? SetName,
    SourceFile SourceFile
  );

  private enum SourceType
  {
    Shared,
    Set,
    Legality,
    Asset
  }
}
