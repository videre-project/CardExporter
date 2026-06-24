/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using CardExporter.MTGO.Rendering.Sets;

namespace CardExporter.MTGO.Rendering.Assets;

internal static class AssetManifestWriter
{
  public static void Write(string manifestPath, IReadOnlyList<AssetManifestEntry> entries)
  {
    using StreamWriter writer = File.CreateText(manifestPath);
    writer.WriteLine("slug,file_name,content_type,source_key,normalized_symbol,conversion_method,byte_count,sha256");

    foreach (AssetManifestEntry entry in entries.OrderBy(entry => entry.Slug, StringComparer.OrdinalIgnoreCase))
    {
      writer.WriteLine(string.Join(',',
        Csv(entry.Slug),
        Csv(entry.FileName),
        Csv(entry.ContentType),
        Csv(entry.SourceKey),
        Csv(entry.NormalizedSymbol),
        Csv(entry.ConversionMethod),
        entry.ByteCount.ToString(CultureInfo.InvariantCulture),
        Csv(entry.Sha256)
      ));
    }
  }

  public static void WriteSetSymbols(
    string manifestPath,
    IReadOnlyList<SetSymbolManifestEntry> entries
  )
  {
    using StreamWriter writer = File.CreateText(manifestPath);
    writer.WriteLine("set_code,rarity,slug,file_name,content_type,resource_name,conversion_method,byte_count,sha256");

    foreach (SetSymbolManifestEntry entry in entries
      .OrderBy(entry => entry.SetCode, StringComparer.OrdinalIgnoreCase)
      .ThenBy(entry => entry.Rarity, StringComparer.OrdinalIgnoreCase)
      .ThenBy(entry => entry.Slug, StringComparer.OrdinalIgnoreCase))
    {
      writer.WriteLine(string.Join(',',
        Csv(entry.SetCode),
        Csv(entry.Rarity),
        Csv(entry.Slug),
        Csv(entry.FileName),
        Csv(entry.ContentType),
        Csv(entry.ResourceName),
        Csv(entry.ConversionMethod),
        entry.ByteCount.ToString(CultureInfo.InvariantCulture),
        Csv(entry.Sha256)
      ));
    }
  }

  private static string Csv(string value) => value.Contains(',') ||
      value.Contains('"') ||
      value.Contains('\n') ||
      value.Contains('\r')
    ? string.Create(CultureInfo.InvariantCulture, $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"")
    : value;
}
