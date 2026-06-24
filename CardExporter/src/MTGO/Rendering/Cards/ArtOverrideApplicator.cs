/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using MTGOSDK.API.Graphics;


namespace CardExporter.MTGO.Rendering.Cards;

internal static class ArtOverrideApplicator
{
  private const int WarmSourceBatchSize = CardImageRenderer.DefaultBatchSize;
  private static readonly object ManifestLock = new object();
  private static readonly HttpClient HttpClient = CreateHttpClient();
  private static bool s_manifestLoaded;
  private static ArtOverrideManifest? s_manifest;

  public static void ApplyForCatalogIds(
    IReadOnlyList<int> catalogIds,
    int renderHeight,
    int renderColumns,
    ILogger logger
  )
  {
    ArtOverrideManifest? manifest = GetManifest(logger);
    if (manifest is null || catalogIds.Count == 0)
    {
      return;
    }

    IReadOnlyList<ArtOverride> overrides = manifest.GetOverridesForCatalogIds(catalogIds);
    if (overrides.Count == 0)
    {
      return;
    }

    string cacheDirectory = ResolveCacheDirectory(manifest.TargetDirectory);
    Directory.CreateDirectory(cacheDirectory);

    int existingCount = 0;
    int copiedCount = 0;
    int downloadedCount = 0;
    int unresolvedCount = 0;
    List<ArtOverride> pendingMtgoCopies = new List<ArtOverride>();

    foreach (ArtOverride artOverride in overrides)
    {
      if (string.Equals(artOverride.SourceType, "scryfall-art-crop", StringComparison.OrdinalIgnoreCase))
      {
        CacheApplyStatus status = EnsureScryfallArtCrop(artOverride, cacheDirectory, logger);
        IncrementStatus(status, ref existingCount, ref downloadedCount, ref unresolvedCount);
        continue;
      }

      if (!string.Equals(artOverride.SourceType, "mtgo-cache-copy", StringComparison.OrdinalIgnoreCase))
      {
        unresolvedCount++;
        logger.LogWarning(
          "Unsupported MTGO art override source type {SourceType} for catalog ID {CatalogId}.",
          artOverride.SourceType,
          artOverride.CatalogId
        );
        continue;
      }

      CacheApplyStatus copyStatus = EnsureMtgoCacheCopy(artOverride, cacheDirectory, logger);
      if (copyStatus == CacheApplyStatus.Unresolved)
      {
        pendingMtgoCopies.Add(artOverride);
        continue;
      }

      IncrementStatus(copyStatus, ref existingCount, ref copiedCount, ref unresolvedCount);
    }

    if (pendingMtgoCopies.Count > 0)
    {
      List<int> sourceCatalogIds = pendingMtgoCopies
        .SelectMany(static artOverride => artOverride.MtgoSources)
        .Select(static source => source.CatalogId)
        .Where(static catalogId => catalogId > 0)
        .Distinct()
        .ToList();
      WarmSourceCache(sourceCatalogIds, renderHeight, renderColumns, logger);

      foreach (ArtOverride artOverride in pendingMtgoCopies)
      {
        CacheApplyStatus copyStatus = EnsureMtgoCacheCopy(artOverride, cacheDirectory, logger);
        IncrementStatus(copyStatus, ref existingCount, ref copiedCount, ref unresolvedCount);
      }
    }

    logger.LogInformation(
      "Applied MTGO art overrides for {OverrideCount} requested cache entries: {ExistingCount} already present, {CopiedCount} copied from MTGO cache, {DownloadedCount} downloaded, {UnresolvedCount} unresolved.",
      overrides.Count,
      existingCount,
      copiedCount,
      downloadedCount,
      unresolvedCount
    );
  }

  private static ArtOverrideManifest? GetManifest(ILogger logger)
  {
    lock (ManifestLock)
    {
      if (s_manifestLoaded)
      {
        return s_manifest;
      }

      s_manifest = ArtOverrideManifest.LoadDefault(logger);
      s_manifestLoaded = true;
      return s_manifest;
    }
  }

  private static CacheApplyStatus EnsureScryfallArtCrop(
    ArtOverride artOverride,
    string cacheDirectory,
    ILogger logger
  )
  {
    string targetPath = Path.Combine(cacheDirectory, artOverride.TargetCacheFile);
    if (IsExpectedScryfallFile(targetPath, artOverride.ScryfallSource))
    {
      return CacheApplyStatus.Existing;
    }

    if (artOverride.ScryfallSource is null)
    {
      logger.LogWarning(
        "Scryfall art override for catalog ID {CatalogId} does not include an art crop source.",
        artOverride.CatalogId
      );
      return CacheApplyStatus.Unresolved;
    }

    try
    {
      byte[] artBytes = HttpClient.GetByteArrayAsync(artOverride.ScryfallSource.ArtCropUrl).GetAwaiter().GetResult();
      if (!MatchesExpectedSource(artBytes, artOverride.ScryfallSource))
      {
        logger.LogWarning(
          "Downloaded Scryfall art crop for catalog ID {CatalogId} did not match the override manifest metadata.",
          artOverride.CatalogId
        );
        return CacheApplyStatus.Unresolved;
      }

      string temporaryPath = targetPath + ".tmp";
      File.WriteAllBytes(temporaryPath, artBytes);
      File.Move(temporaryPath, targetPath, overwrite: true);
      return CacheApplyStatus.Applied;
    }
    catch (Exception exception)
    {
      logger.LogWarning(
        exception,
        "Could not download Scryfall art crop for catalog ID {CatalogId}.",
        artOverride.CatalogId
      );
      return CacheApplyStatus.Unresolved;
    }
  }

  private static CacheApplyStatus EnsureMtgoCacheCopy(
    ArtOverride artOverride,
    string cacheDirectory,
    ILogger logger
  )
  {
    string targetPath = Path.Combine(cacheDirectory, artOverride.TargetCacheFile);
    string? sourcePath = FindSourceCacheFile(cacheDirectory, artOverride.TargetCacheFile);
    if (sourcePath is null)
    {
      return File.Exists(targetPath) ? CacheApplyStatus.Existing : CacheApplyStatus.Unresolved;
    }

    if (FilesHaveSameContent(sourcePath, targetPath))
    {
      return CacheApplyStatus.Existing;
    }

    try
    {
      File.Copy(sourcePath, targetPath, overwrite: true);
      return CacheApplyStatus.Applied;
    }
    catch (Exception exception)
    {
      logger.LogWarning(
        exception,
        "Could not copy MTGO art cache file {SourcePath} to {TargetPath} for catalog ID {CatalogId}.",
        sourcePath,
        targetPath,
        artOverride.CatalogId
      );
      return CacheApplyStatus.Unresolved;
    }
  }

  private static string? FindSourceCacheFile(string cacheDirectory, string targetCacheFile)
  {
    int styleMarkerIndex = targetCacheFile.IndexOf("_sty_", StringComparison.OrdinalIgnoreCase);
    if (styleMarkerIndex < 0)
    {
      return null;
    }

    string sourcePattern = targetCacheFile[..(styleMarkerIndex + "_sty_".Length)] + "*.jpg";
    string targetPath = Path.Combine(cacheDirectory, targetCacheFile);
    return Directory
      .GetFiles(cacheDirectory, sourcePattern)
      .Where(file => !string.Equals(file, targetPath, StringComparison.OrdinalIgnoreCase))
      .OrderBy(static file => file, StringComparer.OrdinalIgnoreCase)
      .FirstOrDefault();
  }

  private static void WarmSourceCache(
    IReadOnlyList<int> sourceCatalogIds,
    int renderHeight,
    int renderColumns,
    ILogger logger
  )
  {
    if (sourceCatalogIds.Count == 0)
    {
      return;
    }

    logger.LogInformation(
      "Warming MTGO art cache from {SourceCatalogIdCount} source catalog IDs for art overrides.",
      sourceCatalogIds.Count
    );
    for (int batchStart = 0; batchStart < sourceCatalogIds.Count; batchStart += WarmSourceBatchSize)
    {
      int[] batch = sourceCatalogIds
        .Skip(batchStart)
        .Take(WarmSourceBatchSize)
        .ToArray();
      try
      {
        _ = CardRenderer.RenderCards(
          batch,
          columns: renderColumns,
          cardHeight: renderHeight
        );
      }
      catch (Exception exception)
      {
        logger.LogWarning(
          exception,
          "Could not warm MTGO art cache for {SourceCatalogIdCount} art override source catalog IDs.",
          batch.Length
        );
      }
    }
  }

  private static string ResolveCacheDirectory(string targetDirectory)
  {
    string? explicitCacheDirectory = Environment.GetEnvironmentVariable("CARDEXPORTER_ART_CACHE_DIRECTORY");
    if (!string.IsNullOrWhiteSpace(explicitCacheDirectory))
    {
      return explicitCacheDirectory;
    }

    string[] targetDirectoryParts = targetDirectory.Split(
      ['/', '\\'],
      StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
    );

    string? cacheRoot = Environment.GetEnvironmentVariable("CARDEXPORTER_MTGO_CACHE_ROOT");
    if (string.IsNullOrWhiteSpace(cacheRoot))
    {
      string localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
      if (string.IsNullOrWhiteSpace(localApplicationData))
      {
        localApplicationData = Path.Combine(
          Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
          "AppData",
          "Local"
        );
      }

      cacheRoot = Path.Combine(localApplicationData, "Wizards of the Coast", "Magic Online");
    }

    return targetDirectoryParts.Aggregate(cacheRoot, Path.Combine);
  }

  private static bool IsExpectedScryfallFile(string path, ScryfallArtSource? source)
  {
    if (source is null || !File.Exists(path))
    {
      return false;
    }

    FileInfo fileInfo = new FileInfo(path);
    if (source.ByteCount is not null && fileInfo.Length != source.ByteCount.Value)
    {
      return false;
    }

    return string.IsNullOrWhiteSpace(source.Sha256) ||
      string.Equals(ComputeSha256(path), source.Sha256, StringComparison.OrdinalIgnoreCase);
  }

  private static bool MatchesExpectedSource(byte[] bytes, ScryfallArtSource source)
  {
    if (source.ByteCount is not null && bytes.LongLength != source.ByteCount.Value)
    {
      return false;
    }

    return string.IsNullOrWhiteSpace(source.Sha256) ||
      string.Equals(ComputeSha256(bytes), source.Sha256, StringComparison.OrdinalIgnoreCase);
  }

  private static bool FilesHaveSameContent(string sourcePath, string targetPath)
  {
    if (!File.Exists(sourcePath) || !File.Exists(targetPath))
    {
      return false;
    }

    FileInfo sourceInfo = new FileInfo(sourcePath);
    FileInfo targetInfo = new FileInfo(targetPath);
    return sourceInfo.Length == targetInfo.Length &&
      string.Equals(ComputeSha256(sourcePath), ComputeSha256(targetPath), StringComparison.OrdinalIgnoreCase);
  }

  private static string ComputeSha256(string path)
  {
    using FileStream stream = File.OpenRead(path);
    using SHA256 sha256 = SHA256.Create();
    return Convert.ToHexString(sha256.ComputeHash(stream)).ToLowerInvariant();
  }

  private static string ComputeSha256(byte[] bytes)
  {
    using SHA256 sha256 = SHA256.Create();
    return Convert.ToHexString(sha256.ComputeHash(bytes)).ToLowerInvariant();
  }

  private static HttpClient CreateHttpClient()
  {
    HttpClient httpClient = new HttpClient();
    httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("CardExporter/1.0");
    return httpClient;
  }

  private static void IncrementStatus(
    CacheApplyStatus status,
    ref int existingCount,
    ref int appliedCount,
    ref int unresolvedCount
  )
  {
    switch (status)
    {
      case CacheApplyStatus.Existing:
        existingCount++;
        break;
      case CacheApplyStatus.Applied:
        appliedCount++;
        break;
      case CacheApplyStatus.Unresolved:
        unresolvedCount++;
        break;
    }
  }

  private enum CacheApplyStatus
  {
    Existing,
    Applied,
    Unresolved
  }
}
