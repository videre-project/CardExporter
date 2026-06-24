/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CardExporter.Database.Postgres;
using CardExporter.Database.R2;
using CardExporter.MTGO.Rendering.Cards;
using Microsoft.Extensions.Logging;


namespace CardExporter.CLI;

internal static class R2UploadCommand
{
  public static async Task<int> ExecuteAsync(
    R2Options options,
    string? connectionString,
    ILogger logger
  )
  {
    List<LocalImageFile> imageFiles = EnumerateImageFiles(options.OutputRoot)
      .OrderBy(static file => file.CatalogId)
      .ThenBy(static file => file.Path, StringComparer.OrdinalIgnoreCase)
      .ToList();

    List<IGrouping<int, LocalImageFile>> duplicateImageFiles = imageFiles
      .GroupBy(static file => file.CatalogId)
      .Where(static group => group.Count() > 1)
      .ToList();
    if (duplicateImageFiles.Count > 0)
    {
      logger.LogError(
        "Found {DuplicateCatalogIdCount} catalog IDs with multiple local PNG files under {OutputRoot}; use a narrower --output-root before uploading. Examples: {CatalogIds}.",
        duplicateImageFiles.Count,
        options.OutputRoot,
        string.Join(", ", duplicateImageFiles.Take(20).Select(static group => group.Key))
      );
      return 1;
    }

    if (imageFiles.Count == 0)
    {
      logger.LogInformation("No local PNG images were found under {OutputRoot}.", options.OutputRoot);
      return 0;
    }

    if (options.DryRun)
    {
      LocalImageValidationSummary validation = await ValidateImageFilesAsync(imageFiles, logger);
      logger.LogInformation(
        "Dry run: would upload {ValidImageCount}/{ImageCount} valid local PNG images from {OutputRoot} to R2 bucket {BucketName}, update {ManifestPath}, and clear matching pending image sync rows when a database connection is configured; rejected {RejectedImageCount}.",
        validation.ValidCount,
        imageFiles.Count,
        options.OutputRoot,
        options.BucketName,
        options.CdnManifestPath,
        validation.RejectedCount
      );
      return validation.RejectedCount == 0 ? 0 : 8;
    }

    CdnManifest manifest = CdnManifest.Load(options.CdnManifestPath);
    IReadOnlyDictionary<int, CardImageKind> imageKinds = await ResolveImageKindsAsync(
      connectionString,
      imageFiles.Select(static file => file.CatalogId).ToList()
    );
    await using R2ImageClient client = R2ImageClient.Create(options);
    int uploadedCount = 0;
    int failedCount = 0;
    int clearedPendingImageSyncRows = 0;
    bool pendingImageSyncCleanupFailed = false;
    List<int> uploadedCatalogIds = new List<int>();

    foreach (LocalImageFile imageFile in imageFiles)
    {
      try
      {
        ValidatedLocalImage? image = await ReadValidImageAsync(imageFile, logger);
        if (image is null)
        {
          failedCount++;
          continue;
        }

        CardImageKind kind = imageKinds.GetValueOrDefault(imageFile.CatalogId, CardImageKind.Card);
        await client.UploadPngAsync(imageFile.CatalogId, kind, image.PngBytes);
        manifest.UpsertImage(imageFile.CatalogId, options.PublicBaseUrl, DateTimeOffset.UtcNow, kind);
        uploadedCatalogIds.Add(imageFile.CatalogId);
        uploadedCount++;
      }
      catch (Exception exception)
      {
        failedCount++;
        logger.LogError(exception, "Failed to upload image for catalog ID {CatalogId} from {Path}.", imageFile.CatalogId, imageFile.Path);
      }
    }

    if (uploadedCount > 0)
    {
      manifest.Write(options.CdnManifestPath);
    }

    if (!string.IsNullOrWhiteSpace(connectionString) && uploadedCatalogIds.Count > 0)
    {
      try
      {
        clearedPendingImageSyncRows = await PendingImageSyncRepository.ClearAsync(
          connectionString,
          uploadedCatalogIds
        );
      }
      catch (Exception exception)
      {
        pendingImageSyncCleanupFailed = true;
        logger.LogError(
          exception,
          "Failed to clear pending image sync rows for uploaded R2 image catalog IDs."
        );
      }
    }

    logger.LogInformation(
      "Uploaded {UploadedCount}/{ImageCount} local PNG images to R2 bucket {BucketName}; cleared {PendingImageSyncRowCount} pending image sync rows; failed {FailedCount}.",
      uploadedCount,
      imageFiles.Count,
      options.BucketName,
      clearedPendingImageSyncRows,
      failedCount
    );
    return failedCount == 0 && !pendingImageSyncCleanupFailed ? 0 : 8;
  }

  private static async Task<IReadOnlyDictionary<int, CardImageKind>> ResolveImageKindsAsync(
    string? connectionString,
    IReadOnlyCollection<int> catalogIds
  )
  {
    if (string.IsNullOrWhiteSpace(connectionString) || catalogIds.Count == 0)
    {
      return new Dictionary<int, CardImageKind>();
    }

    return await CardImageCatalogRepository.GetImageKindsAsync(connectionString, catalogIds);
  }

  private static async Task<LocalImageValidationSummary> ValidateImageFilesAsync(
    IReadOnlyList<LocalImageFile> imageFiles,
    ILogger logger
  )
  {
    int validCount = 0;
    int rejectedCount = 0;
    foreach (LocalImageFile imageFile in imageFiles)
    {
      if (await ReadValidImageAsync(imageFile, logger) is null)
      {
        rejectedCount++;
        continue;
      }

      validCount++;
    }

    return new LocalImageValidationSummary(validCount, rejectedCount);
  }

  private static async Task<ValidatedLocalImage?> ReadValidImageAsync(
    LocalImageFile imageFile,
    ILogger logger
  )
  {
    byte[] imageBytes;
    try
    {
      imageBytes = await File.ReadAllBytesAsync(imageFile.Path);
    }
    catch (Exception exception)
    {
      logger.LogError(exception, "Could not read image for catalog ID {CatalogId} from {Path}.", imageFile.CatalogId, imageFile.Path);
      return null;
    }

    if (!RenderedCardImageInspector.IsSupportedPng(imageBytes))
    {
      logger.LogError("Skipping catalog ID {CatalogId} from {Path}; file is not a supported PNG image.", imageFile.CatalogId, imageFile.Path);
      return null;
    }

    if (!RenderedCardImageInspector.TryReadDimensions(imageBytes, out RenderedCardImageInspector.PngDimensions dimensions) ||
        dimensions.Height != ImageExportOptions.DefaultCardHeight)
    {
      logger.LogError(
        "Skipping catalog ID {CatalogId} from {Path}; image height is {ImageHeight}px but R2 uploads require {RequiredImageHeight}px.",
        imageFile.CatalogId,
        imageFile.Path,
        dimensions.Height,
        ImageExportOptions.DefaultCardHeight
      );
      return null;
    }

    if (RenderedCardImageInspector.HasLikelyMissingArt(imageBytes, out string diagnostic))
    {
      logger.LogError(
        "Skipping catalog ID {CatalogId} from {Path}; image appears to be missing card art ({Diagnostic}).",
        imageFile.CatalogId,
        imageFile.Path,
        diagnostic
      );
      return null;
    }

    return new ValidatedLocalImage(imageFile.CatalogId, imageBytes);
  }

  private static IEnumerable<LocalImageFile> EnumerateImageFiles(string outputRoot)
  {
    if (!Directory.Exists(outputRoot))
    {
      yield break;
    }

    foreach (string file in Directory.EnumerateFiles(outputRoot, "*.png", SearchOption.AllDirectories))
    {
      if (CardImageKey.TryParseCatalogId(file, out int catalogId))
      {
        yield return new LocalImageFile(catalogId, file);
      }
    }
  }

  private sealed record LocalImageFile(
    int CatalogId,
    string Path
  );

  private sealed record ValidatedLocalImage(
    int CatalogId,
    byte[] PngBytes
  );

  private readonly record struct LocalImageValidationSummary(
    int ValidCount,
    int RejectedCount
  );
}
