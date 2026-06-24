/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CardExporter.MTGO.Rendering;
using CardExporter.MTGO.Rendering.Mana;
using Microsoft.Extensions.Logging;
using MTGOSDK.Win32;


namespace CardExporter.CLI;

internal static class ManaSymbolExportCommand
{
  public static Task<int> ExecuteAsync(ManaSymbolExportOptions options, ILogger logger)
  {
    string appDirectory = ResolveAppDirectory(options);
    string cardAssemblyPath = Path.Combine(appDirectory, "Card.dll");
    if (!File.Exists(cardAssemblyPath))
    {
      logger.LogError("Card.dll was not found at {CardAssemblyPath}. Pass --mtgo-app-dir if MTGO is installed elsewhere.", cardAssemblyPath);
      return Task.FromResult(2);
    }

    Directory.CreateDirectory(options.OutputRoot);

    ManaSymbolBamlExtractor extractor = new ManaSymbolBamlExtractor();
    string xaml = extractor.ExtractManaSymbolsXaml(cardAssemblyPath);

    string xamlPath = Path.Combine(options.OutputRoot, "manasymbols.xaml");
    File.WriteAllText(xamlPath, xaml);

    var resources = ManaSymbolResourceDiscovery.Discover(xaml);
    string manifestPath = Path.Combine(options.OutputRoot, "manifest.csv");
    WriteManifest(manifestPath, resources);

    logger.LogInformation(
      "Exported {SymbolCount} mana symbol resource entries from {CardAssemblyPath} to {OutputRoot}.",
      resources.Count,
      cardAssemblyPath,
      options.OutputRoot
    );
    logger.LogInformation("Wrote {XamlPath} and {ManifestPath}.", xamlPath, manifestPath);

    return Task.FromResult(0);
  }

  private static string ResolveAppDirectory(ManaSymbolExportOptions options)
  {
    if (!string.IsNullOrWhiteSpace(options.MTGOAppDirectory))
    {
      return Path.GetFullPath(options.MTGOAppDirectory);
    }

    if (!string.IsNullOrWhiteSpace(Constants.MTGOAppDirectory))
    {
      return Constants.MTGOAppDirectory;
    }

    throw new InvalidOperationException("MTGOAppDirectory could not be resolved. Start MTGO once, or pass --mtgo-app-dir.");
  }

  private static void WriteManifest(string manifestPath, System.Collections.Generic.IReadOnlyList<ManaSymbolResource> resources)
  {
    using StreamWriter writer = File.CreateText(manifestPath);
    writer.WriteLine("slug,normalized_symbol,mtgo_key,element_name");

    foreach (ManaSymbolResource resource in resources.OrderBy(resource => resource.Slug, StringComparer.OrdinalIgnoreCase))
    {
      writer.WriteLine(string.Join(',',
        Csv(resource.Slug),
        Csv(resource.NormalizedSymbol),
        Csv(resource.MTGOKey),
        Csv(resource.ElementName)
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
