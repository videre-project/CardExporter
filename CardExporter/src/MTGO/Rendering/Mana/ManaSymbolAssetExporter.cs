/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.IO;

using CardExporter.MTGO.Rendering;
using CardExporter.MTGO.Rendering.Assets;

namespace CardExporter.MTGO.Rendering.Mana;

internal sealed class ManaSymbolAssetExporter
{
  private readonly string _manaSymbolsXaml;
  private readonly IReadOnlyDictionary<string, byte[]> _embeddedImages;
  private readonly string _cardAssemblyPath;

  public ManaSymbolAssetExporter(
    string manaSymbolsXaml,
    IReadOnlyDictionary<string, byte[]> embeddedImages,
    string cardAssemblyPath
  )
  {
    _manaSymbolsXaml = manaSymbolsXaml;
    _embeddedImages = embeddedImages;
    _cardAssemblyPath = cardAssemblyPath;
  }

  public int ExportTo(string outputRoot)
  {
    AssetFileWriter.ResetDirectory(outputRoot);

    WpfSvgAssetExporter exporter = new WpfSvgAssetExporter(_manaSymbolsXaml, _embeddedImages);
    ColorlessManaSymbolSvgExporter colorlessExporter = new ColorlessManaSymbolSvgExporter(_cardAssemblyPath);
    List<AssetManifestEntry> manifestEntries = new List<AssetManifestEntry>();
    foreach (ManaSymbolResource resource in ManaSymbolResourceDiscovery.Discover(_manaSymbolsXaml))
    {
      SvgAsset asset = Convert(resource, exporter, colorlessExporter);
      string fileName = AssetFileWriter.Write(outputRoot, asset);
      manifestEntries.Add(AssetManifestEntry.FromAsset(
        asset,
        fileName,
        resource.MTGOKey,
        resource.NormalizedSymbol
      ));
    }

    AssetManifestWriter.Write(Path.Combine(outputRoot, "manifest.csv"), manifestEntries);
    return manifestEntries.Count;
  }

  private SvgAsset Convert(
    ManaSymbolResource resource,
    WpfSvgAssetExporter exporter,
    ColorlessManaSymbolSvgExporter colorlessExporter
  )
  {
    if (string.Equals(resource.ElementName, "ColorlessManaSymbolTemplate", StringComparison.OrdinalIgnoreCase))
    {
      return colorlessExporter.Convert(resource);
    }

    return string.Equals(resource.ElementName, "EmbeddedImage", StringComparison.OrdinalIgnoreCase)
      ? ConvertEmbeddedImageResource(resource)
      : exporter.ConvertManaSymbol(resource);
  }

  private SvgAsset ConvertEmbeddedImageResource(ManaSymbolResource resource)
  {
    string imageKey = resource.MTGOKey;
    int assemblySeparator = imageKey.IndexOf(':', StringComparison.Ordinal);
    if (assemblySeparator >= 0)
    {
      imageKey = imageKey[(assemblySeparator + 1)..];
    }

    imageKey = NormalizeResourceKey(imageKey);
    if (!_embeddedImages.TryGetValue(imageKey, out byte[]? imageBytes))
    {
      throw new InvalidOperationException($"Embedded mana symbol image {resource.MTGOKey} was not found.");
    }

    return new SvgAsset(
      resource.Slug,
      "png",
      "image/png",
      RasterImageConverter.ToPng(imageBytes),
      "embedded-raster-converted-to-png"
    );
  }

  private static string NormalizeResourceKey(string key) =>
    key.Replace('\\', '/').TrimStart('/');
}
