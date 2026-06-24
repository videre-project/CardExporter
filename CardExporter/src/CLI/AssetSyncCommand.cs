/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using CardExporter.Database.R2;
using CardExporter.MTGO.Files;
using Microsoft.Extensions.Logging;


namespace CardExporter.CLI;

internal static class AssetSyncCommand
{
  private static readonly string[] AssetPrefixes =
  [
    "card-counters",
    "mana-symbols",
    "player-counters",
    "set-symbols"
  ];

  public static async Task<int> ExecuteAsync(
    MTGOAssetExportOptions assetOptions,
    R2Options r2Options,
    string sourceManifestRoot,
    ILogger logger
  )
  {
    string appDirectory = MTGOAssetExportCommand.ResolveAppDirectory(assetOptions);
    IReadOnlyList<SourceFile> assetSourceFiles = AssetSourceFiles.Enumerate(appDirectory);
    SourceManifestComparison sourceComparison = SourceManifestPreflight.CompareAssets(
      sourceManifestRoot,
      assetSourceFiles
    );
    CdnManifest manifest = CdnManifest.Load(r2Options.CdnManifestPath);
    bool hasManifestAssetRows = AssetPrefixes.All(manifest.ContainsKeyPrefix);
    if (!sourceComparison.HasChanges && hasManifestAssetRows)
    {
      logger.LogInformation(
        "MTGO asset sources are unchanged across {AssetSourceFileCount} files and {ManifestPath} already tracks all CDN asset prefixes; skipping asset export.",
        sourceComparison.SourceFileCount,
        r2Options.CdnManifestPath
      );
      return 0;
    }

    if (!hasManifestAssetRows)
    {
      logger.LogInformation(
        "CDN manifest {ManifestPath} is missing one or more MTGO asset prefixes; asset export will run.",
        r2Options.CdnManifestPath
      );
    }
    else if (sourceComparison.HasChanges)
    {
      logger.LogInformation(
        "MTGO asset source manifest changed; asset export will run: {Reason}.",
        sourceComparison.Reason
      );
    }

    string outputRoot = assetOptions.OutputRoot;
    string? temporaryOutputRoot = null;
    try
    {
      if (r2Options.DryRun)
      {
        temporaryOutputRoot = Path.Combine(
          Path.GetTempPath(),
          "cardexporter-assets-" + Guid.NewGuid().ToString("N")
        );
        outputRoot = temporaryOutputRoot;
        logger.LogInformation(
          "Dry run: generating MTGO assets under temporary output root {OutputRoot}; no R2 objects or manifests will be changed.",
          outputRoot
        );
      }

      int exportResult = await MTGOAssetExportCommand.ExecuteAsync(
        assetOptions with { OutputRoot = outputRoot },
        logger
      );
      if (exportResult != 0)
      {
        return exportResult;
      }

      List<CdnAssetFile> assetFiles = EnumerateAssetFiles(outputRoot)
        .OrderBy(static file => file.Key, StringComparer.Ordinal)
        .ToList();
      if (assetFiles.Count == 0)
      {
        logger.LogInformation("No generated MTGO CDN assets were found under {OutputRoot}.", outputRoot);
        return 0;
      }

      List<CdnAssetFile> changedAssets = assetFiles
        .Where(file => ShouldUpload(file, manifest))
        .ToList();

      if (r2Options.DryRun)
      {
        if (changedAssets.Count == 0)
        {
          logger.LogInformation(
            "Dry run: 0/{AssetCount} generated MTGO CDN assets would upload to R2 bucket {BucketName}; {ManifestPath} is current.",
            assetFiles.Count,
            r2Options.BucketName,
            r2Options.CdnManifestPath
          );
        }
        else
        {
          logger.LogInformation(
            "Dry run: {ChangedAssetCount}/{AssetCount} generated MTGO CDN assets would upload to R2 bucket {BucketName} and update {ManifestPath}. First keys: {Keys}.",
            changedAssets.Count,
            assetFiles.Count,
            r2Options.BucketName,
            r2Options.CdnManifestPath,
            string.Join(", ", changedAssets.Take(25).Select(static file => file.Key))
          );
        }

        return 0;
      }

      if (changedAssets.Count == 0)
      {
        SourceManifestWriter.WriteAssets(sourceManifestRoot, appDirectory, logger);
        logger.LogInformation(
          "All {AssetCount} generated MTGO CDN assets are already current in {ManifestPath}.",
          assetFiles.Count,
          r2Options.CdnManifestPath
        );
        return 0;
      }

      await using R2ImageClient client = R2ImageClient.Create(r2Options);
      int uploadedCount = 0;
      int failedCount = 0;
      foreach (CdnAssetFile asset in changedAssets)
      {
        try
        {
          await client.UploadObjectAsync(asset.Key, asset.ContentType, asset.Bytes);
          manifest.UpsertAsset(
            asset.Key,
            r2Options.PublicBaseUrl,
            DateTimeOffset.UtcNow,
            asset.ContentType,
            asset.Bytes.Length,
            asset.Sha256
          );
          uploadedCount++;
        }
        catch (Exception exception)
        {
          failedCount++;
          logger.LogError(exception, "Failed to upload CDN asset {Key} from {Path}.", asset.Key, asset.Path);
        }
      }

      if (uploadedCount > 0)
      {
        manifest.Write(r2Options.CdnManifestPath);
      }
      if (failedCount == 0)
      {
        SourceManifestWriter.WriteAssets(sourceManifestRoot, appDirectory, logger);
      }

      logger.LogInformation(
        "Uploaded {UploadedCount}/{ChangedAssetCount} changed MTGO CDN assets to R2 bucket {BucketName}; total generated assets {AssetCount}; failed {FailedCount}.",
        uploadedCount,
        changedAssets.Count,
        r2Options.BucketName,
        assetFiles.Count,
        failedCount
      );
      return failedCount == 0 ? 0 : 8;
    }
    finally
    {
      if (!string.IsNullOrWhiteSpace(temporaryOutputRoot) && Directory.Exists(temporaryOutputRoot))
      {
        Directory.Delete(temporaryOutputRoot, recursive: true);
      }
    }
  }

  private static bool ShouldUpload(CdnAssetFile file, CdnManifest manifest)
  {
    if (!manifest.TryGet(file.Key, out CdnManifestRow? row))
    {
      return true;
    }

    if (!string.IsNullOrWhiteSpace(row.Sha256))
    {
      return !string.Equals(row.Sha256, file.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    return row.ByteCount != file.Bytes.Length ||
      !string.Equals(row.ContentType, file.ContentType, StringComparison.OrdinalIgnoreCase);
  }

  private static IEnumerable<CdnAssetFile> EnumerateAssetFiles(string outputRoot)
  {
    foreach (string prefix in AssetPrefixes)
    {
      string directory = Path.Combine(outputRoot, prefix);
      if (!Directory.Exists(directory))
      {
        continue;
      }

      foreach (string file in Directory.EnumerateFiles(directory))
      {
        string fileName = Path.GetFileName(file);
        if (string.Equals(fileName, "manifest.csv", StringComparison.OrdinalIgnoreCase))
        {
          continue;
        }

        string? contentType = GetContentType(fileName);
        if (contentType is null)
        {
          continue;
        }

        byte[] bytes = File.ReadAllBytes(file);
        yield return new CdnAssetFile(
          Key: $"{prefix}/{fileName}",
          Path: file,
          ContentType: contentType,
          Bytes: bytes,
          Sha256: Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()
        );
      }
    }
  }

  private static string? GetContentType(string fileName)
  {
    string extension = Path.GetExtension(fileName);
    if (string.Equals(extension, ".svg", StringComparison.OrdinalIgnoreCase))
    {
      return "image/svg+xml";
    }

    if (string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase))
    {
      return "image/png";
    }

    return null;
  }

  private sealed record CdnAssetFile(
    string Key,
    string Path,
    string ContentType,
    byte[] Bytes,
    string Sha256
  );
}
