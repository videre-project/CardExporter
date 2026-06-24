/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;


namespace CardExporter.MTGO.Rendering.Cards;

internal sealed class ArtOverrideManifest
{
  private const string ManifestDirectoryName = "manifests";
  private const string DefaultManifestFileName = "mtgo-art-overrides.xml";
  private const string MtgoCacheCopySourceType = "mtgo-cache-copy";
  private const string ScryfallArtCropSourceType = "scryfall-art-crop";

  private readonly Dictionary<int, List<ArtOverride>> _overridesByCatalogId;

  private ArtOverrideManifest(
    string manifestPath,
    string targetDirectory,
    IReadOnlyList<ArtOverride> overrides
  )
  {
    ManifestPath = manifestPath;
    TargetDirectory = targetDirectory;
    Overrides = overrides;
    _overridesByCatalogId = BuildOverrideIndex(overrides);
  }

  public string ManifestPath { get; }
  public string TargetDirectory { get; }
  public IReadOnlyList<ArtOverride> Overrides { get; }

  public static ArtOverrideManifest? LoadDefault(ILogger logger)
  {
    string? manifestPath = ResolveDefaultManifestPath(logger);
    if (manifestPath is null)
    {
      return null;
    }

    try
    {
      XDocument document = XDocument.Load(manifestPath, LoadOptions.PreserveWhitespace);
      XElement root = document.Root ?? throw new InvalidDataException("Art override manifest is empty.");
      string targetDirectory = GetRequiredAttribute(root, "targetDirectory");
      ValidateTargetDirectory(targetDirectory);
      List<ArtOverride> overrides = root
        .Elements("override")
        .Select(ParseOverride)
        .ToList();

      logger.LogInformation(
        "Loaded {OverrideCount} MTGO art overrides from {ManifestPath}.",
        overrides.Count,
        manifestPath
      );
      return new ArtOverrideManifest(manifestPath, targetDirectory, overrides);
    }
    catch (Exception exception)
    {
      logger.LogWarning(exception, "Could not load MTGO art override manifest {ManifestPath}.", manifestPath);
      return null;
    }
  }

  public IReadOnlyList<ArtOverride> GetOverridesForCatalogIds(IReadOnlyList<int> catalogIds)
  {
    Dictionary<string, ArtOverride> selectedOverrides = new Dictionary<string, ArtOverride>(StringComparer.OrdinalIgnoreCase);
    foreach (int catalogId in catalogIds)
    {
      if (!_overridesByCatalogId.TryGetValue(catalogId, out List<ArtOverride>? overrides))
      {
        continue;
      }

      foreach (ArtOverride artOverride in overrides)
      {
        selectedOverrides[artOverride.TargetCacheFile] = artOverride;
      }
    }

    return selectedOverrides.Values.ToList();
  }

  private static string? ResolveDefaultManifestPath(ILogger logger)
  {
    string? configuredPath = Environment.GetEnvironmentVariable("CARDEXPORTER_ART_OVERRIDES");
    if (!string.IsNullOrWhiteSpace(configuredPath))
    {
      if (string.Equals(configuredPath, "none", StringComparison.OrdinalIgnoreCase) ||
          string.Equals(configuredPath, "off", StringComparison.OrdinalIgnoreCase))
      {
        return null;
      }

      if (File.Exists(configuredPath))
      {
        return configuredPath;
      }

      logger.LogWarning(
        "CARDEXPORTER_ART_OVERRIDES is set to {ManifestPath}, but that file does not exist.",
        configuredPath
      );
      return configuredPath;
    }

    foreach (string candidatePath in EnumerateDefaultManifestPathCandidates())
    {
      if (File.Exists(candidatePath))
      {
        return candidatePath;
      }
    }

    return null;
  }

  private static IEnumerable<string> EnumerateDefaultManifestPathCandidates()
  {
    HashSet<string> candidatePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (string root in EnumerateSearchRoots())
    {
      DirectoryInfo? directory = new DirectoryInfo(root);
      while (directory is not null)
      {
        string candidatePath = Path.Combine(
          directory.FullName,
          ManifestDirectoryName,
          DefaultManifestFileName
        );
        if (candidatePaths.Add(candidatePath))
        {
          yield return candidatePath;
        }

        directory = directory.Parent;
      }
    }
  }

  private static IEnumerable<string> EnumerateSearchRoots()
  {
    if (!string.IsNullOrWhiteSpace(Environment.CurrentDirectory))
    {
      yield return Environment.CurrentDirectory;
    }

    if (!string.IsNullOrWhiteSpace(AppContext.BaseDirectory))
    {
      yield return AppContext.BaseDirectory;
    }
  }

  private static Dictionary<int, List<ArtOverride>> BuildOverrideIndex(IReadOnlyList<ArtOverride> overrides)
  {
    Dictionary<int, List<ArtOverride>> index = new Dictionary<int, List<ArtOverride>>();
    foreach (ArtOverride artOverride in overrides)
    {
      foreach (int catalogId in artOverride.TargetCatalogIds)
      {
        if (!index.TryGetValue(catalogId, out List<ArtOverride>? catalogOverrides))
        {
          catalogOverrides = new List<ArtOverride>();
          index[catalogId] = catalogOverrides;
        }

        catalogOverrides.Add(artOverride);
      }
    }

    return index;
  }

  private static ArtOverride ParseOverride(XElement element)
  {
    int catalogId = ParseRequiredPositiveIntAttribute(element, "catalogId");
    string targetCacheFile = GetRequiredAttribute(element, "targetCacheFile");
    ValidateCacheFileName(targetCacheFile);
    string sourceType = ParseSourceType(GetRequiredAttribute(element, "sourceType"));
    HashSet<int> targetCatalogIds = ParseCatalogIdList((string?)element.Attribute("targetCatalogIds"));
    targetCatalogIds.Add(catalogId);

    List<MtgoArtSource> mtgoSources = element
      .Element("mtgoSources")?
      .Elements("mtgoSource")
      .Select(ParseMtgoSource)
      .ToList() ?? [];

    return new ArtOverride(
      catalogId,
      targetCatalogIds,
      (string?)element.Attribute("setCode"),
      (string?)element.Attribute("name"),
      ParseOptionalPositiveIntAttribute(element, "artId"),
      targetCacheFile,
      sourceType,
      mtgoSources,
      ParseScryfallSource(element.Element("scryfallSource"))
    );
  }

  private static MtgoArtSource ParseMtgoSource(XElement element)
  {
    return new MtgoArtSource(
      ParseRequiredPositiveIntAttribute(element, "catalogId"),
      (string?)element.Attribute("setCode"),
      ParseOptionalPositiveIntAttribute(element, "artId")
    );
  }

  private static ScryfallArtSource? ParseScryfallSource(XElement? element)
  {
    if (element is null)
    {
      return null;
    }

    string? artCropUrl = (string?)element.Attribute("artCrop");
    if (string.IsNullOrWhiteSpace(artCropUrl))
    {
      return null;
    }

    if (!Uri.TryCreate(artCropUrl, UriKind.Absolute, out Uri? artCropUri) ||
        (artCropUri.Scheme != Uri.UriSchemeHttp && artCropUri.Scheme != Uri.UriSchemeHttps))
    {
      throw new InvalidDataException("Scryfall art crop URL must be an absolute HTTP(S) URL.");
    }

    long? byteCount = ParseOptionalLongAttribute(element, "byteCount");
    if (byteCount is <= 0)
    {
      throw new InvalidDataException("Scryfall art crop byteCount must be greater than zero.");
    }

    string? sha256 = (string?)element.Attribute("sha256");
    if (!string.IsNullOrWhiteSpace(sha256) && !IsSha256Hex(sha256))
    {
      throw new InvalidDataException("Scryfall art crop sha256 must be 64 hexadecimal characters.");
    }

    return new ScryfallArtSource(
      artCropUri.ToString(),
      byteCount,
      sha256
    );
  }

  private static HashSet<int> ParseCatalogIdList(string? rawValue)
  {
    HashSet<int> catalogIds = new HashSet<int>();
    if (string.IsNullOrWhiteSpace(rawValue))
    {
      return catalogIds;
    }

    foreach (string part in rawValue.Split(
      [',', ';', ' ', '\t', '\r', '\n'],
      StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
    ))
    {
      if (int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out int catalogId) && catalogId > 0)
      {
        catalogIds.Add(catalogId);
      }
    }

    return catalogIds;
  }

  private static string GetRequiredAttribute(XElement element, string attributeName)
  {
    string? value = (string?)element.Attribute(attributeName);
    if (string.IsNullOrWhiteSpace(value))
    {
      throw new InvalidDataException($"Art override element is missing required attribute '{attributeName}'.");
    }

    return value;
  }

  private static void ValidateCacheFileName(string fileName)
  {
    if (fileName is "." or ".." ||
        Path.IsPathRooted(fileName) ||
        fileName.Contains('/', StringComparison.Ordinal) ||
        fileName.Contains('\\', StringComparison.Ordinal) ||
        fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
        ContainsWindowsInvalidFileNameCharacter(fileName))
    {
      throw new InvalidDataException($"Art override target cache file must be a file name, not a path: {fileName}");
    }

    if (!fileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
    {
      throw new InvalidDataException($"Art override target cache file must be a .jpg file: {fileName}");
    }
  }

  private static void ValidateTargetDirectory(string targetDirectory)
  {
    if (Path.IsPathRooted(targetDirectory))
    {
      throw new InvalidDataException($"Art override target directory must be relative: {targetDirectory}");
    }

    string[] pathSegments = targetDirectory.Split(
      ['/', '\\'],
      StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
    );
    if (pathSegments.Length == 0)
    {
      throw new InvalidDataException("Art override target directory must not be empty.");
    }

    foreach (string segment in pathSegments)
    {
      if (segment is "." or ".." ||
          segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
          ContainsWindowsInvalidFileNameCharacter(segment))
      {
        throw new InvalidDataException($"Art override target directory contains an invalid path segment: {targetDirectory}");
      }
    }
  }

  private static bool ContainsWindowsInvalidFileNameCharacter(string value)
  {
    return value.IndexOfAny(['<', '>', ':', '"', '|', '?', '*']) >= 0;
  }

  private static bool IsSha256Hex(string value)
  {
    if (value.Length != 64)
    {
      return false;
    }

    foreach (char character in value)
    {
      if (!Uri.IsHexDigit(character))
      {
        return false;
      }
    }

    return true;
  }

  private static string ParseSourceType(string sourceType)
  {
    if (string.Equals(sourceType, MtgoCacheCopySourceType, StringComparison.Ordinal) ||
        string.Equals(sourceType, ScryfallArtCropSourceType, StringComparison.Ordinal))
    {
      return sourceType;
    }

    throw new InvalidDataException(
      $"Art override sourceType must be {MtgoCacheCopySourceType} or {ScryfallArtCropSourceType}: {sourceType}"
    );
  }

  private static int ParseRequiredPositiveIntAttribute(XElement element, string attributeName)
  {
    string value = GetRequiredAttribute(element, attributeName);
    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ||
        parsed <= 0)
    {
      throw new InvalidDataException($"Art override attribute '{attributeName}' must be a positive integer.");
    }

    return parsed;
  }

  private static int? ParseOptionalPositiveIntAttribute(XElement element, string attributeName)
  {
    string? value = (string?)element.Attribute(attributeName);
    if (string.IsNullOrWhiteSpace(value))
    {
      return null;
    }

    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) &&
        parsed > 0)
    {
      return parsed;
    }

    throw new InvalidDataException($"Art override attribute '{attributeName}' must be a positive integer.");
  }

  private static long? ParseOptionalLongAttribute(XElement element, string attributeName)
  {
    string? value = (string?)element.Attribute(attributeName);
    if (string.IsNullOrWhiteSpace(value))
    {
      return null;
    }

    if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed))
    {
      return parsed;
    }

    throw new InvalidDataException($"Art override attribute '{attributeName}' must be an integer.");
  }
}

internal sealed record ArtOverride(
  int CatalogId,
  IReadOnlySet<int> TargetCatalogIds,
  string? SetCode,
  string? Name,
  int? ArtId,
  string TargetCacheFile,
  string SourceType,
  IReadOnlyList<MtgoArtSource> MtgoSources,
  ScryfallArtSource? ScryfallSource
);

internal sealed record MtgoArtSource(
  int CatalogId,
  string? SetCode,
  int? ArtId
);

internal sealed record ScryfallArtSource(
  string ArtCropUrl,
  long? ByteCount,
  string? Sha256
);
