/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Generic;
using System.IO;

using CardExporter.MTGO.Rendering;
using CardExporter.MTGO.Rendering.Assets;

namespace CardExporter.MTGO.Rendering.Counters;

internal sealed class CardCounterAssetExporter
{
  private const string SourceResourceName = "resources/counterresourcedictionary.baml";

  private readonly string _countersXaml;
  private readonly IReadOnlyDictionary<string, byte[]> _embeddedImages;

  public CardCounterAssetExporter(
    string countersXaml,
    IReadOnlyDictionary<string, byte[]> embeddedImages
  )
  {
    _countersXaml = countersXaml;
    _embeddedImages = embeddedImages;
  }

  public int ExportTo(string outputRoot)
  {
    AssetFileWriter.ResetDirectory(outputRoot);

    WpfSvgAssetExporter exporter = new WpfSvgAssetExporter(_countersXaml, _embeddedImages);
    List<AssetManifestEntry> manifestEntries = new List<AssetManifestEntry>();
    foreach (SvgAsset asset in exporter.ConvertCardCounters())
    {
      string fileName = AssetFileWriter.Write(outputRoot, asset);
      manifestEntries.Add(AssetManifestEntry.FromAsset(
        asset,
        fileName,
        SourceResourceName,
        normalizedSymbol: string.Empty
      ));
    }

    AssetManifestWriter.Write(Path.Combine(outputRoot, "manifest.csv"), manifestEntries);
    return manifestEntries.Count;
  }
}
