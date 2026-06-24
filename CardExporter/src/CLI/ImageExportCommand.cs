/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.IO;
using CardExporter.MTGO.Rendering.Cards;
using Microsoft.Extensions.Logging;
using MTGOSDK.API;


namespace CardExporter.CLI;

internal static class ImageExportCommand
{
  private const int BatchSize = CardImageRenderer.DefaultBatchSize;

  public static int Execute(ImageExportOptions options, ILogger logger)
  {
    if (options.CatalogIds.Count > 0 && !string.IsNullOrWhiteSpace(options.SetCode))
    {
      logger.LogError("--catalog-ids and --set are mutually exclusive.");
      return 7;
    }

    if (options.CatalogIds.Count > 0)
    {
      ExportCatalogIds(options.CatalogIds, options.OutputRoot, options.CardHeight, options.RenderColumns, logger);
      logger.LogInformation("Catalog ID export completed successfully.");
      return 0;
    }

    if (!string.IsNullOrWhiteSpace(options.SetCode))
    {
      dynamic cardDataManager = GetCardDataManager();
      dynamic cardSets = cardDataManager.AllCardSetsByCode;
      ExportSet(cardSets, options.SetCode.Trim(), options.OutputRoot, options.CardHeight, options.RenderColumns, logger);
      logger.LogInformation("Set export completed successfully.");
      return 0;
    }

    ExportMissingCards(options.OutputRoot, options.CardHeight, options.RenderColumns, logger);
    logger.LogInformation("All missing card image batches rendered successfully.");
    return 0;
  }

  private static dynamic GetCardDataManager()
  {
    return ObjectProvider.Get("WotC.MtGO.Client.Model.Core.ICardDataManager");
  }

  private static void ExportCatalogIds(
    IReadOnlyList<int> catalogIds,
    string outputRoot,
    int renderHeight,
    int renderColumns,
    ILogger logger
  )
  {
    string outputDir = Path.Combine(outputRoot, "_catalog_ids");
    Directory.CreateDirectory(outputDir);

    int totalBatches = (int)Math.Ceiling((double)catalogIds.Count / BatchSize);
    int savedCards = 0;
    int missingCards = 0;
    var exportStopwatch = System.Diagnostics.Stopwatch.StartNew();

    logger.LogInformation(
      "Processing {CatalogIdCount} requested catalog IDs at {RenderHeight}px height.",
      catalogIds.Count,
      renderHeight
    );

    for (int batchStart = 0, batchIndex = 0; batchStart < catalogIds.Count; batchStart += BatchSize, batchIndex++)
    {
      int currentBatchSize = Math.Min(BatchSize, catalogIds.Count - batchStart);
      int[] batchArray = new int[currentBatchSize];
      for (int i = 0; i < currentBatchSize; i++)
      {
        batchArray[i] = catalogIds[batchStart + i];
      }

      savedCards += RenderBatch(
        batchArray,
        outputDir,
        renderHeight,
        renderColumns,
        logger,
        ref missingCards
      );

      logger.LogInformation(
        "Requested catalog IDs: completed batch {BatchIndex}/{TotalBatches}; saved {SavedCards}/{CatalogIdCount} images.",
        batchIndex + 1,
        totalBatches,
        savedCards,
        catalogIds.Count
      );

      CollectGarbage();
    }

    logger.LogInformation(
      "Catalog ID export complete. Saved {SavedCards}/{CatalogIdCount} images to {OutputDir} at {RenderHeight}px height; missing {MissingCards}; elapsed {Elapsed}.",
      savedCards,
      catalogIds.Count,
      outputDir,
      renderHeight,
      missingCards,
      exportStopwatch.Elapsed.ToString(@"hh\:mm\:ss")
    );
  }

  private static void ExportSet(
    dynamic cardSets,
    string requestedSetCode,
    string outputRoot,
    int renderHeight,
    int renderColumns,
    ILogger logger
  )
  {
    string? actualSetCode = ResolveSetCode(cardSets, requestedSetCode);
    if (actualSetCode is null)
    {
      throw new InvalidOperationException(
        $"Set code '{requestedSetCode}' was not found. Available set codes include: {PreviewSetCodes(cardSets)}"
      );
    }

    dynamic cardSet = cardSets[actualSetCode];
    dynamic catalogIds = cardSet.m_cardsByCatId.Keys;
    List<int> catalogIdList = new List<int>();
    foreach (dynamic catalogId in catalogIds)
    {
      catalogIdList.Add((int)catalogId);
    }

    string outputDir = Path.Combine(outputRoot, actualSetCode);
    Directory.CreateDirectory(outputDir);

    int totalBatches = (int)Math.Ceiling((double)catalogIdList.Count / BatchSize);
    int savedCards = 0;
    int missingCards = 0;
    var setStopwatch = System.Diagnostics.Stopwatch.StartNew();

    logger.LogInformation(
      "Processing set {SetCode} with {CatalogIdCount} catalog IDs at {RenderHeight}px height.",
      actualSetCode,
      catalogIdList.Count,
      renderHeight
    );

    for (int batchStart = 0, batchIndex = 0; batchStart < catalogIdList.Count; batchStart += BatchSize, batchIndex++)
    {
      int currentBatchSize = Math.Min(BatchSize, catalogIdList.Count - batchStart);
      int[] batchArray = new int[currentBatchSize];
      catalogIdList.CopyTo(batchStart, batchArray, 0, currentBatchSize);

      savedCards += RenderBatch(
        batchArray,
        outputDir,
        renderHeight,
        renderColumns,
        logger,
        ref missingCards
      );

      logger.LogInformation(
        "Set {SetCode}: completed batch {BatchIndex}/{TotalBatches}; saved {SavedCards}/{CatalogIdCount} images.",
        actualSetCode,
        batchIndex + 1,
        totalBatches,
        savedCards,
        catalogIdList.Count
      );

      CollectGarbage();
    }

    logger.LogInformation(
      "Set {SetCode} export complete. Saved {SavedCards}/{CatalogIdCount} images to {OutputDir} at {RenderHeight}px height; missing {MissingCards}; elapsed {Elapsed}.",
      actualSetCode,
      savedCards,
      catalogIdList.Count,
      outputDir,
      renderHeight,
      missingCards,
      setStopwatch.Elapsed.ToString(@"hh\:mm\:ss")
    );
  }

  private static void ExportMissingCards(
    string outputRoot,
    int renderHeight,
    int renderColumns,
    ILogger logger
  )
  {
    var globalStopwatch = System.Diagnostics.Stopwatch.StartNew();
    dynamic cardDataManager = GetCardDataManager();
    dynamic objects = cardDataManager.DigitalObjectsByCatId;
    dynamic objectList = objects.Keys;
    int objectCount = objectList.Count;

    logger.LogInformation("Scanning for existing renders in {OutputRoot}...", outputRoot);
    HashSet<string> existingFiles = FindExistingRenderedCatalogIds(outputRoot, logger);

    logger.LogInformation("Resolving object collection with {ObjectCount} objects.", objectCount);
    List<int> pendingCatalogIds = new List<int>(BatchSize);
    string uncatalogedDir = Path.Combine(outputRoot, "_uncataloged");
    Directory.CreateDirectory(uncatalogedDir);

    int processedCards = 0;
    int savedCards = 0;
    int missingCards = 0;
    var exportStopwatch = System.Diagnostics.Stopwatch.StartNew();

    for (int i = 0; i < objectCount; i++)
    {
      int catalogId = (int)objectList[i];
      if (!existingFiles.Contains(catalogId.ToString()))
      {
        pendingCatalogIds.Add(catalogId);
      }

      if (pendingCatalogIds.Count < BatchSize && (i + 1 != objectCount || pendingCatalogIds.Count == 0))
      {
        continue;
      }

      int[] batchArray = pendingCatalogIds.ToArray();
      pendingCatalogIds.Clear();

      savedCards += RenderBatch(
        batchArray,
        uncatalogedDir,
        renderHeight,
        renderColumns,
        logger,
        ref missingCards
      );

      processedCards += batchArray.Length;
      double averageMilliseconds = exportStopwatch.Elapsed.TotalMilliseconds / processedCards;
      logger.LogInformation(
        "Exported {ProcessedCards} missing card images so far. Average speed: {AverageMilliseconds:F2}ms/card.",
        processedCards,
        averageMilliseconds
      );

      CollectGarbage();
    }

    logger.LogInformation(
      "Missing card export complete. Saved {SavedCards} images to {OutputDir}; missing {MissingCards}; elapsed {Elapsed}.",
      savedCards,
      uncatalogedDir,
      missingCards,
      globalStopwatch.Elapsed.ToString(@"hh\:mm\:ss")
    );
  }

  private static int RenderBatch(
    int[] batchArray,
    string outputDir,
    int renderHeight,
    int renderColumns,
    ILogger logger,
    ref int missingCards
  )
  {
    RenderedCardBatch renderedBatch = CardImageRenderer.RenderCards(
      batchArray,
      renderHeight,
      renderColumns,
      logger
    );
    missingCards += renderedBatch.MissingCount;

    int savedCards = 0;
    foreach (RenderedCardImage image in renderedBatch.Images)
    {
      string outputPath = Path.Combine(outputDir, $"{image.CatalogId}.png");
      if (SaveRenderedCardBytes(image.PngBytes, outputPath, image.CatalogId, logger))
      {
        savedCards++;
      }
    }

    return savedCards;
  }

  private static string? ResolveSetCode(dynamic cardSets, string requestedSetCode)
  {
    foreach (dynamic setCode in cardSets.Keys)
    {
      string? currentSetCode = Convert.ToString(setCode);
      if (string.Equals(currentSetCode, requestedSetCode, StringComparison.OrdinalIgnoreCase))
      {
        return currentSetCode;
      }
    }

    return null;
  }

  private static string PreviewSetCodes(dynamic cardSets)
  {
    List<string> availableSetCodes = new List<string>();
    foreach (dynamic setCode in cardSets.Keys)
    {
      string? currentSetCode = Convert.ToString(setCode);
      if (!string.IsNullOrWhiteSpace(currentSetCode))
      {
        availableSetCodes.Add(currentSetCode);
      }
    }

    availableSetCodes.Sort(StringComparer.OrdinalIgnoreCase);
    int previewCount = Math.Min(40, availableSetCodes.Count);
    return string.Join(", ", availableSetCodes.GetRange(0, previewCount));
  }

  private static HashSet<string> FindExistingRenderedCatalogIds(string outputRoot, ILogger logger)
  {
    HashSet<string> existingFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    if (!Directory.Exists(outputRoot))
    {
      return existingFiles;
    }

    Stack<string> directories = new Stack<string>(20);
    directories.Push(outputRoot);

    while (directories.Count > 0)
    {
      string currentDirectory = directories.Pop();
      foreach (string directory in EnumerateDirectories(currentDirectory, logger))
      {
        directories.Push(directory);
      }

      foreach (string file in EnumerateFiles(currentDirectory, logger))
      {
        string fileName = Path.GetFileNameWithoutExtension(file);
        existingFiles.Add(fileName);
        if (fileName.EndsWith("-300px", StringComparison.OrdinalIgnoreCase))
        {
          existingFiles.Add(fileName[..^6]);
        }
      }
    }

    return existingFiles;
  }

  private static IEnumerable<string> EnumerateDirectories(string directory, ILogger logger)
  {
    try
    {
      return Directory.GetDirectories(directory);
    }
    catch (UnauthorizedAccessException exception)
    {
      logger.LogWarning(exception, "Could not read directory {Directory}.", directory);
      return [];
    }
    catch (DirectoryNotFoundException exception)
    {
      logger.LogWarning(exception, "Directory disappeared while scanning {Directory}.", directory);
      return [];
    }
  }

  private static IEnumerable<string> EnumerateFiles(string directory, ILogger logger)
  {
    try
    {
      return Directory.GetFiles(directory, "*.png");
    }
    catch (UnauthorizedAccessException exception)
    {
      logger.LogWarning(exception, "Could not read files in {Directory}.", directory);
      return [];
    }
    catch (DirectoryNotFoundException exception)
    {
      logger.LogWarning(exception, "Directory disappeared while scanning {Directory}.", directory);
      return [];
    }
  }

  private static bool SaveRenderedCardBytes(byte[] imageBytes, string outputPath, int catalogId, ILogger logger)
  {
    try
    {
      using FileStream fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
      fileStream.Write(imageBytes, 0, imageBytes.Length);
      return true;
    }
    catch (Exception exception)
    {
      logger.LogError(exception, "Failed to save image for catalog ID {CatalogId}.", catalogId);
      return false;
    }
  }

  private static void CollectGarbage()
  {
    GC.Collect();
    GC.WaitForPendingFinalizers();
  }
}
