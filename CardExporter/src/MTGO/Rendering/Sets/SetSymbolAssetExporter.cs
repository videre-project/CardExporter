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

namespace CardExporter.MTGO.Rendering.Sets;

internal sealed class SetSymbolAssetExporter
{
  private readonly IReadOnlyList<EmbeddedResourceFile> _cardResources;

  public SetSymbolAssetExporter(IReadOnlyList<EmbeddedResourceFile> cardResources)
  {
    _cardResources = cardResources;
  }

  public int ExportTo(string outputRoot)
  {
    AssetFileWriter.ResetDirectory(outputRoot);

    List<SetSymbolManifestEntry> manifestEntries = new List<SetSymbolManifestEntry>();
    foreach (EmbeddedResourceFile resource in _cardResources
      .Where(resource => IsSetSymbolResource(resource.Key))
      .OrderBy(resource => resource.Key, StringComparer.OrdinalIgnoreCase))
    {
      SetSymbolName name = ParseName(resource.Key);
      bool sourceIsPng = resource.Key.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
      SvgAsset asset = new SvgAsset(
        name.Slug,
        "png",
        "image/png",
        sourceIsPng ? resource.Bytes : RasterImageConverter.ToPng(resource.Bytes),
        sourceIsPng ? "embedded-raster-png" : "embedded-raster-converted-to-png"
      );

      string fileName = AssetFileWriter.Write(outputRoot, asset);
      manifestEntries.Add(SetSymbolManifestEntry.FromAsset(
        name,
        asset,
        fileName,
        $"{resource.AssemblyName}:{resource.Key}"
      ));
    }

    AssetManifestWriter.WriteSetSymbols(Path.Combine(outputRoot, "manifest.csv"), manifestEntries);
    return manifestEntries.Count;
  }

  private static bool IsSetSymbolResource(string key) =>
    NormalizeResourceKey(key).StartsWith(
      "imagefiles/frameassets/setsymbols/",
      StringComparison.OrdinalIgnoreCase
    ) && EmbeddedResourceFilters.IsImageResource(key);

  private static SetSymbolName ParseName(string resourceKey)
  {
    string baseName = Path.GetFileNameWithoutExtension(NormalizeResourceKey(resourceKey));
    foreach (string rarity in new[] { "Timeshifted", "Uncommon", "Common", "Mythic", "Bonus", "Rare" })
    {
      if (!baseName.EndsWith(rarity, StringComparison.OrdinalIgnoreCase) ||
          baseName.Length == rarity.Length)
      {
        continue;
      }

      string setCode = baseName[..^rarity.Length].ToUpperInvariant();
      string normalizedRarity = rarity.ToLowerInvariant();
      return new SetSymbolName(
        setCode,
        normalizedRarity,
        $"{setCode}-{normalizedRarity}"
      );
    }

    return new SetSymbolName(
      string.Empty,
      string.Empty,
      ToSlug(baseName)
    );
  }

  private static string NormalizeResourceKey(string key) =>
    key.Replace('\\', '/').TrimStart('/');

  private static string ToSlug(string value)
  {
    List<char> chars = new List<char>(value.Length);
    bool lastWasSeparator = false;
    foreach (char character in value)
    {
      if (char.IsLetterOrDigit(character))
      {
        chars.Add(char.ToLowerInvariant(character));
        lastWasSeparator = false;
        continue;
      }

      if (!lastWasSeparator && chars.Count > 0)
      {
        chars.Add('-');
        lastWasSeparator = true;
      }
    }

    if (chars.Count > 0 && chars[^1] == '-')
    {
      chars.RemoveAt(chars.Count - 1);
    }

    return new string(chars.ToArray());
  }
}
