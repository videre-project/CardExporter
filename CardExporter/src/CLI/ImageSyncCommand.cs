/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using CardExporter.Database.Postgres;
using CardExporter.Database.R2;
using CardExporter.MTGO.Rendering.Cards;
using Microsoft.Extensions.Logging;


namespace CardExporter.CLI;

internal static class ImageSyncCommand
{
  private const int CardHeight = ImageExportOptions.DefaultCardHeight;
  private const int RenderColumns = ImageExportOptions.DefaultRenderColumns;

  public static async Task<int> ExecuteAsync(
    string connectionString,
    R2Options options,
    ILogger logger
  )
  {
    return await ExecuteAsync(
      connectionString,
      options,
      logger,
      CardImageSyncScope.MissingOnly()
    );
  }

  public static async Task<int> ExecuteAsync(
    string connectionString,
    R2Options options,
    ILogger logger,
    CardImageSyncScope scope
  )
  {
    ImportDatabaseState databaseState = await ImportDatabaseState.ReadAsync(connectionString);
    if (!databaseState.HasCardData)
    {
      logger.LogError(
        "Database is missing imported card data; run import before syncing images. Current counts: {SetCount} sets, {CardCount} cards, {ProductCount} products, {OracleCardCount} oracle cards.",
        databaseState.SetCount,
        databaseState.CardCount,
        databaseState.ProductCount,
        databaseState.OracleCardCount
      );
      return 5;
    }

    CdnManifest manifest = CdnManifest.Load(options.CdnManifestPath);
    MissingCardImageCatalog missingImages = await CardImageCatalogRepository.GetCatalogIdsForSyncAsync(
      connectionString,
      manifest.ImageCatalogIds,
      scope
    );
    IReadOnlyList<CardImageCatalogEntry> missingEntries = missingImages.Entries;
    IReadOnlyList<int> missingCatalogIds = missingImages.CatalogIds;
    Dictionary<int, CardImageKind> imageKinds = missingEntries
      .GroupBy(static entry => entry.CatalogId)
      .ToDictionary(
        static group => group.Key,
        static group => group.First().Kind
      );

    if (scope.ClearPendingOnSuccess && scope.CatalogIds.Count > 0 && !options.DryRun)
    {
      HashSet<int> syncableCatalogIds = missingCatalogIds.ToHashSet();
      List<int> obsoletePendingCatalogIds = scope.CatalogIds
        .Where(catalogId => !syncableCatalogIds.Contains(catalogId))
        .ToList();
      if (obsoletePendingCatalogIds.Count > 0)
      {
        await PendingImageSyncRepository.ClearAsync(connectionString, obsoletePendingCatalogIds);
        logger.LogInformation(
          "Cleared {ObsoletePendingImageCount} pending image sync IDs that are already satisfied by the manifest or no longer required by the database.",
          obsoletePendingCatalogIds.Count
        );
      }
    }

    if (missingCatalogIds.Count == 0)
    {
      logger.LogInformation(
        "{SyncDescription}: no image catalog IDs need syncing for manifest {ManifestPath}.",
        scope.Description,
        options.CdnManifestPath
      );
      return 0;
    }

    if (options.DryRun)
    {
      logger.LogInformation(
        "Foil clone image policy: ordinary foil clones reuse parent renders; separate foil clone renders are kept for sets {SetCodes}.",
        string.Join(", ", FoilCloneImagePolicy.SeparateFoilCloneImageSetCodes)
      );
      logger.LogInformation(
        "Dry run: {MissingImageCount} image catalog IDs need syncing for {SyncDescription} against {ManifestPath}. First catalog IDs: {CatalogIds}.",
        missingCatalogIds.Count,
        scope.Description,
        options.CdnManifestPath,
        string.Join(", ", missingCatalogIds.Take(25))
      );
      LogMissingSetSummary(missingImages.SetSummaries, logger);
      return 0;
    }

    await using R2ImageClient client = R2ImageClient.Create(options);
    var stopwatch = Stopwatch.StartNew();
    int uploadedCount = 0;
    int failedUploadCount = 0;
    int missingRenderCount = 0;

    logger.LogInformation(
      "Rendering and uploading {MissingImageCount} image catalog IDs for {SyncDescription} to R2 bucket {BucketName}.",
      missingCatalogIds.Count,
      scope.Description,
      options.BucketName
    );

    for (int batchStart = 0, batchIndex = 0; batchStart < missingCatalogIds.Count; batchStart += CardImageRenderer.DefaultBatchSize, batchIndex++)
    {
      List<int> batch = missingCatalogIds
        .Skip(batchStart)
        .Take(CardImageRenderer.DefaultBatchSize)
        .ToList();
      RenderedCardBatch renderedBatch = CardImageRenderer.RenderCards(
        batch,
        CardHeight,
        RenderColumns,
        logger
      );
      missingRenderCount += renderedBatch.MissingCount;

      int uploadedInBatch = 0;
      List<int> uploadedCatalogIdsInBatch = new List<int>();
      foreach (RenderedCardImage image in renderedBatch.Images)
      {
        try
        {
          CardImageKind kind = imageKinds.GetValueOrDefault(image.CatalogId, CardImageKind.Card);
          await client.UploadPngAsync(image.CatalogId, kind, image.PngBytes);
          manifest.UpsertImage(image.CatalogId, options.PublicBaseUrl, DateTimeOffset.UtcNow, kind);
          uploadedCatalogIdsInBatch.Add(image.CatalogId);
          uploadedCount++;
          uploadedInBatch++;
        }
        catch (Exception exception)
        {
          failedUploadCount++;
          logger.LogError(exception, "Failed to upload image for catalog ID {CatalogId}.", image.CatalogId);
        }
      }

      if (uploadedInBatch > 0)
      {
        manifest.Write(options.CdnManifestPath);
        if (scope.ClearPendingOnSuccess)
        {
          await PendingImageSyncRepository.ClearAsync(connectionString, uploadedCatalogIdsInBatch);
        }
      }

      logger.LogInformation(
        "Image sync batch {BatchIndex}: uploaded {UploadedCount}/{TotalCount}; failed uploads {FailedUploadCount}; missing renders {MissingRenderCount}.",
        batchIndex + 1,
        uploadedCount,
        missingCatalogIds.Count,
        failedUploadCount,
        missingRenderCount
      );

      CollectGarbage();
    }

    logger.LogInformation(
      "Image sync complete. Uploaded {UploadedCount}/{TotalCount} image catalog IDs to R2 in {Elapsed}; failed uploads {FailedUploadCount}; missing renders {MissingRenderCount}.",
      uploadedCount,
      missingCatalogIds.Count,
      stopwatch.Elapsed.ToString(@"hh\:mm\:ss"),
      failedUploadCount,
      missingRenderCount
    );
    return failedUploadCount == 0 && missingRenderCount == 0 ? 0 : 8;
  }

  private static void CollectGarbage()
  {
    GC.Collect();
    GC.WaitForPendingFinalizers();
  }

  private static void LogMissingSetSummary(
    IReadOnlyList<MissingCardImageSetSummary> summaries,
    ILogger logger
  )
  {
    if (summaries.Count == 0)
    {
      return;
    }

    logger.LogInformation("Largest missing image gaps by set:");
    foreach (MissingCardImageSetSummary summary in summaries.Take(25))
    {
      logger.LogInformation(
        "  {SetCode}: {MissingCount} missing | {ReleaseDate} | {SetName} | catalog IDs {FirstCatalogId}-{LastCatalogId}",
        FormatSetCode(summary.SetCode),
        summary.MissingCount,
        FormatReleaseDate(summary.ReleaseDate),
        summary.SetName ?? "(unknown set)",
        summary.FirstCatalogId,
        summary.LastCatalogId
      );
    }

    logger.LogInformation("Newest sets with missing images:");
    foreach (MissingCardImageSetSummary summary in summaries
      .OrderByDescending(static summary => summary.ReleaseDate ?? DateOnly.MinValue)
      .ThenBy(static summary => summary.SetCode, StringComparer.OrdinalIgnoreCase)
      .Take(15))
    {
      logger.LogInformation(
        "  {ReleaseDate} | {SetCode} | {MissingCount} missing | {SetName}",
        FormatReleaseDate(summary.ReleaseDate),
        FormatSetCode(summary.SetCode),
        summary.MissingCount,
        summary.SetName ?? "(unknown set)"
      );
    }
  }

  private static string FormatSetCode(string? setCode)
  {
    return string.IsNullOrWhiteSpace(setCode) ? "(none)" : setCode;
  }

  private static string FormatReleaseDate(DateOnly? releaseDate)
  {
    return releaseDate?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) ?? "unknown";
  }
}
