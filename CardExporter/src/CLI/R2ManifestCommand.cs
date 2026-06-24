/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CardExporter.Database.R2;
using Microsoft.Extensions.Logging;


namespace CardExporter.CLI;

internal static class R2ManifestCommand
{
  public static async Task<int> ExecuteAsync(R2Options options, ILogger logger)
  {
    await using R2ImageClient client = R2ImageClient.Create(options);
    IReadOnlyList<R2Object> objects = await client.ListObjectsAsync();
    CdnManifest previousManifest = CdnManifest.Load(options.CdnManifestPath);
    CdnManifest manifest = CdnManifest.Empty();

    foreach (R2Object item in objects.OrderBy(static item => item.Key, CdnManifestKeyComparer.Instance))
    {
      manifest.UpsertObject(item, options.PublicBaseUrl, previousManifest);
    }

    if (options.DryRun)
    {
      logger.LogInformation(
        "Dry run: would write {ObjectCount} R2 CDN manifest rows to {ManifestPath}.",
        manifest.Count,
        options.CdnManifestPath
      );

      return 0;
    }

    manifest.Write(options.CdnManifestPath);
    logger.LogInformation(
      "Wrote {ObjectCount} R2 CDN manifest rows to {ManifestPath}.",
      manifest.Count,
      options.CdnManifestPath
    );

    return 0;
  }
}
