/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using CardExporter.MTGO.Rendering;
using CardExporter.MTGO.Rendering.Assets;

namespace CardExporter.MTGO.Rendering.Counters;

internal sealed class PlayerCounterAssetExporter
{
  private readonly IReadOnlyList<EmbeddedResourceFile> _duelSceneResources;

  public PlayerCounterAssetExporter(IReadOnlyList<EmbeddedResourceFile> duelSceneResources)
  {
    _duelSceneResources = duelSceneResources;
  }

  public int ExportTo(string outputRoot)
  {
    AssetFileWriter.ResetDirectory(outputRoot);

    List<AssetManifestEntry> manifestEntries = new List<AssetManifestEntry>();
    foreach (EmbeddedResourceFile resource in _duelSceneResources
      .Where(resource => resource.Key.StartsWith("imagefiles/playercounter_", StringComparison.OrdinalIgnoreCase))
      .OrderBy(resource => resource.Key, StringComparer.OrdinalIgnoreCase))
    {
      SvgAsset asset = ConvertResource(resource);
      string fileName = AssetFileWriter.Write(outputRoot, asset);
      manifestEntries.Add(AssetManifestEntry.FromAsset(
        asset,
        fileName,
        $"DuelScene.dll:{resource.Key}",
        normalizedSymbol: string.Empty
      ));
    }

    AssetManifestWriter.Write(Path.Combine(outputRoot, "manifest.csv"), manifestEntries);
    return manifestEntries.Count;
  }

  private static SvgAsset ConvertResource(EmbeddedResourceFile resource)
  {
    string slug = Path.GetFileNameWithoutExtension(resource.Key)
      .Replace("playercounter_", string.Empty, StringComparison.OrdinalIgnoreCase);
    bool sourceIsPng = resource.Key.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
    return new SvgAsset(
      slug,
      "png",
      "image/png",
      sourceIsPng ? resource.Bytes : RasterImageConverter.ToPng(resource.Bytes),
      sourceIsPng ? "embedded-raster-png" : "embedded-raster-converted-to-png"
    );
  }
}
