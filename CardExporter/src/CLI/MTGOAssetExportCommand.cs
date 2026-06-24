/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CardExporter.MTGO.Rendering;
using CardExporter.MTGO.Rendering.Assets;
using CardExporter.MTGO.Rendering.Counters;
using CardExporter.MTGO.Rendering.Mana;
using CardExporter.MTGO.Rendering.Sets;
using Microsoft.Extensions.Logging;
using MTGOSDK.Win32;


namespace CardExporter.CLI;

internal static class MTGOAssetExportCommand
{
  private const string ManaSymbolsResourceName = "manasymbols.baml";
  private const string CounterResourceName = "resources/counterresourcedictionary.baml";

  public static Task<int> ExecuteAsync(MTGOAssetExportOptions options, ILogger logger)
  {
    string appDirectory = ResolveAppDirectory(options);
    string cardAssemblyPath = Path.Combine(appDirectory, "Card.dll");
    string duelSceneAssemblyPath = Path.Combine(appDirectory, "DuelScene.dll");

    if (!File.Exists(cardAssemblyPath))
    {
      logger.LogError("Card.dll was not found at {CardAssemblyPath}. Pass --mtgo-app-dir if MTGO is installed elsewhere.", cardAssemblyPath);
      return Task.FromResult(2);
    }

    if (!File.Exists(duelSceneAssemblyPath))
    {
      logger.LogError("DuelScene.dll was not found at {DuelSceneAssemblyPath}. Pass --mtgo-app-dir if MTGO is installed elsewhere.", duelSceneAssemblyPath);
      return Task.FromResult(2);
    }

    Directory.CreateDirectory(options.OutputRoot);

    BamlResourceExtractor bamlExtractor = new BamlResourceExtractor();
    string manaSymbolsXaml = bamlExtractor.ExtractXaml(cardAssemblyPath, ManaSymbolsResourceName);
    string countersXaml = bamlExtractor.ExtractXaml(duelSceneAssemblyPath, CounterResourceName);
    WriteSourceXaml(options.OutputRoot, manaSymbolsXaml, countersXaml);

    IReadOnlyList<EmbeddedResourceFile> cardResources = EmbeddedResourceReader
      .Enumerate(cardAssemblyPath)
      .ToList();
    IReadOnlyList<EmbeddedResourceFile> duelSceneResources = EmbeddedResourceReader
      .Enumerate(duelSceneAssemblyPath)
      .Where(resource => EmbeddedResourceFilters.IsImageResource(resource.Key))
      .ToList();
    Dictionary<string, byte[]> duelSceneImages = duelSceneResources.ToDictionary(
      resource => resource.Key,
      resource => resource.Bytes,
      StringComparer.OrdinalIgnoreCase
    );

    int manaSymbolCount = new ManaSymbolAssetExporter(
      manaSymbolsXaml,
      duelSceneImages,
      cardAssemblyPath
    ).ExportTo(Path.Combine(options.OutputRoot, "mana-symbols"));
    int cardCounterCount = new CardCounterAssetExporter(
      countersXaml,
      duelSceneImages
    ).ExportTo(Path.Combine(options.OutputRoot, "card-counters"));
    int playerCounterCount = new PlayerCounterAssetExporter(
      duelSceneResources
    ).ExportTo(Path.Combine(options.OutputRoot, "player-counters"));
    int setSymbolCount = new SetSymbolAssetExporter(
      cardResources
    ).ExportTo(Path.Combine(options.OutputRoot, "set-symbols"));

    logger.LogInformation(
      "Exported {ManaSymbolCount} mana symbols, {CardCounterCount} card counters, {PlayerCounterCount} player counters, and {SetSymbolCount} set symbols to {OutputRoot}.",
      manaSymbolCount,
      cardCounterCount,
      playerCounterCount,
      setSymbolCount,
      options.OutputRoot
    );

    return Task.FromResult(0);
  }

  internal static string ResolveAppDirectory(MTGOAssetExportOptions options)
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

  private static void WriteSourceXaml(
    string outputRoot,
    string manaSymbolsXaml,
    string countersXaml
  )
  {
    string sourceRoot = Path.Combine(outputRoot, "source");
    Directory.CreateDirectory(sourceRoot);
    File.WriteAllText(Path.Combine(sourceRoot, "manasymbols.xaml"), manaSymbolsXaml);
    File.WriteAllText(Path.Combine(sourceRoot, "counterresourcedictionary.xaml"), countersXaml);
  }
}
