/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CardExporter.Database.Postgres;
using CardExporter.Database.R2;
using Microsoft.Extensions.Logging;


namespace CardExporter.CLI;

internal static class R2DeleteCommand
{
  public static async Task<int> ExecuteAsync(
    R2Options options,
    IReadOnlyList<int> catalogIds,
    string? connectionString,
    ILogger logger
  )
  {
    List<int> requestedCatalogIds = catalogIds
      .Distinct()
      .OrderBy(static catalogId => catalogId)
      .ToList();
    if (requestedCatalogIds.Count == 0)
    {
      logger.LogError("r2-delete requires --catalog-ids.");
      return 1;
    }

    if (options.DryRun)
    {
      logger.LogInformation(
        "Dry run: would delete {ImageCount} image objects from R2 bucket {BucketName}, remove matching rows from {ManifestPath}, and clear matching pending image sync rows when a database connection is configured. Catalog IDs: {CatalogIds}.",
        requestedCatalogIds.Count,
        options.BucketName,
        options.CdnManifestPath,
        string.Join(", ", requestedCatalogIds)
      );
      return 0;
    }

    CdnManifest manifest = CdnManifest.Load(options.CdnManifestPath);
    IReadOnlyDictionary<int, CardImageKind> imageKinds = await ResolveImageKindsAsync(
      connectionString,
      requestedCatalogIds
    );
    await using R2ImageClient client = R2ImageClient.Create(options);
    int deletedCount = 0;
    int removedManifestRows = 0;
    int failedCount = 0;
    int clearedPendingImageSyncRows = 0;
    bool pendingImageSyncCleanupFailed = false;
    List<int> deletedCatalogIds = new List<int>();

    foreach (int catalogId in requestedCatalogIds)
    {
      try
      {
        CardImageKind kind = imageKinds.GetValueOrDefault(catalogId, CardImageKind.Card);
        await client.DeletePngAsync(catalogId, kind);
        deletedCatalogIds.Add(catalogId);
        deletedCount++;
        if (manifest.RemoveImage(catalogId))
        {
          removedManifestRows++;
        }
      }
      catch (Exception exception)
      {
        failedCount++;
        logger.LogError(exception, "Failed to delete image for catalog ID {CatalogId} from R2.", catalogId);
      }
    }

    if (removedManifestRows > 0)
    {
      manifest.Write(options.CdnManifestPath);
    }

    if (!string.IsNullOrWhiteSpace(connectionString) && deletedCatalogIds.Count > 0)
    {
      try
      {
        clearedPendingImageSyncRows = await PendingImageSyncRepository.ClearAsync(
          connectionString,
          deletedCatalogIds
        );
      }
      catch (Exception exception)
      {
        pendingImageSyncCleanupFailed = true;
        logger.LogError(
          exception,
          "Failed to clear pending image sync rows for deleted R2 image catalog IDs."
        );
      }
    }

    logger.LogInformation(
      "Deleted {DeletedCount}/{ImageCount} image objects from R2 bucket {BucketName}; removed {ManifestRowCount} manifest rows; cleared {PendingImageSyncRowCount} pending image sync rows; failed {FailedCount}.",
      deletedCount,
      requestedCatalogIds.Count,
      options.BucketName,
      removedManifestRows,
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
}
