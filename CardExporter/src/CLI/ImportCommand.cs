/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CardExporter.Database.Postgres;
using CardExporter.MTGO;
using CardExporter.MTGO.Files;
using CardExporter.MTGO.Parsing;
using Microsoft.Extensions.Logging;


namespace CardExporter.CLI;

internal static class ImportCommand
{
  public static async Task<int> ExecuteAsync(
    string dataDirectory,
    ImportPipelineAction action,
    CommandLineOptions options,
    ILoggerFactory loggerFactory,
    ILogger logger
  )
  {
    string? connectionString = ResolveConnectionString(options);
    if (string.IsNullOrWhiteSpace(connectionString))
    {
      logger.LogError("A database connection string is required. Set CARDEXPORTER_DATABASE_URL or pass --connection-string.");
      return 5;
    }

    IReadOnlyDictionary<string, SetMetadata> setMetadata = options.StartClient
      ? SetMetadataLoader.Load(dataDirectory, logger)
      : new Dictionary<string, SetMetadata>(StringComparer.OrdinalIgnoreCase);

    if (!options.StartClient)
    {
      logger.LogInformation("Skipping runtime set metadata enrichment because MTGO was not started by this import run.");
    }

    var parser = new Parser(
      dataDirectory,
      loggerFactory.CreateLogger<Parser>(),
      setMetadata
    );
    var writer = new PostgresCardDataWriter(
      connectionString,
      loggerFactory.CreateLogger<PostgresCardDataWriter>()
    );

    if (action == ImportPipelineAction.ImportLegalities)
    {
      ImportDatabaseState finalDatabaseState = await writer.ImportLegalitiesAsync(parser);
      SourceManifestWriter.WriteLegalities(
        options.SourceManifestRoot,
        parser,
        CreateManifestImportCounts(finalDatabaseState),
        logger
      );
      return 0;
    }

    CardDataImportResult importResult = await writer.ImportAsync(parser);
    SourceManifestWriter.Write(
      options.SourceManifestRoot,
      parser,
      CreateManifestImportCounts(importResult.FinalDatabaseState),
      logger
    );
    if (options.SyncImages)
    {
      logger.LogInformation(
        "Recorded {AddedImageCatalogIdCount} added image catalog IDs and {ModifiedImageCatalogIdCount} modified image catalog IDs as pending image sync work.",
        importResult.ImageChanges.AddedCatalogIds.Count,
        importResult.ImageChanges.ModifiedCatalogIds.Count
      );
      int imageSyncResult = await SyncPendingImagesAsync(connectionString, options, logger);
      if (imageSyncResult != 0)
      {
        return imageSyncResult;
      }

      return await SyncAssetsIfRequestedAsync(options, logger);
    }

    return await SyncAssetsIfRequestedAsync(options, logger);
  }


  public static async Task<int> SyncPendingImagesAsync(
    CommandLineOptions options,
    ILogger logger
  )
  {
    string? connectionString = ResolveConnectionString(options);
    if (string.IsNullOrWhiteSpace(connectionString))
    {
      logger.LogError("A database connection string is required. Set CARDEXPORTER_DATABASE_URL or pass --connection-string.");
      return 5;
    }

    return await SyncPendingImagesAsync(connectionString, options, logger);
  }

  public static async Task<int> SyncPendingImagesAsync(
    string connectionString,
    CommandLineOptions options,
    ILogger logger
  )
  {
    IReadOnlyList<PendingImageSyncEntry> pendingEntries = await PendingImageSyncRepository.GetEntriesAsync(
      connectionString,
      createIfMissing: !options.R2.DryRun
    );
    if (pendingEntries.Count == 0)
    {
      logger.LogInformation("No pending image sync catalog IDs were found.");
      return 0;
    }

    HashSet<int> addedCatalogIds = pendingEntries
      .Where(static entry => PendingImageSyncReasons.IsAdded(entry.Reason))
      .Select(static entry => entry.CatalogId)
      .ToHashSet();
    HashSet<int> modifiedCatalogIds = pendingEntries
      .Where(static entry => !PendingImageSyncReasons.IsAdded(entry.Reason))
      .Select(static entry => entry.CatalogId)
      .ToHashSet();
    addedCatalogIds.ExceptWith(modifiedCatalogIds);

    logger.LogInformation(
      "Found {AddedPendingImageSyncCount} added and {ModifiedPendingImageSyncCount} modified pending image sync catalog IDs.",
      addedCatalogIds.Count,
      modifiedCatalogIds.Count
    );

    int result = 0;
    if (modifiedCatalogIds.Count > 0)
    {
      result = await ImageSyncCommand.ExecuteAsync(
        connectionString,
        options.R2,
        logger,
        CardImageSyncScope.PendingModifiedCatalogIds(modifiedCatalogIds)
      );
    }

    if (addedCatalogIds.Count > 0)
    {
      int addedResult = await ImageSyncCommand.ExecuteAsync(
        connectionString,
        options.R2,
        logger,
        CardImageSyncScope.PendingAddedCatalogIds(addedCatalogIds)
      );
      if (result == 0)
      {
        result = addedResult;
      }
    }

    return result;
  }

  public static string? ResolveConnectionString(CommandLineOptions options)
  {
    return options.ConnectionString ?? Environment.GetEnvironmentVariable("CARDEXPORTER_DATABASE_URL");
  }

  private static SourceManifestImportCounts CreateManifestImportCounts(
    ImportDatabaseState databaseState
  )
  {
    return new SourceManifestImportCounts(
      databaseState.SetCount,
      databaseState.CardCount,
      databaseState.ProductCount,
      databaseState.CardCatalogVariantCount,
      databaseState.OracleCardCount,
      databaseState.CardFaceCount,
      databaseState.LegalityCount
    );
  }

  private static async Task<int> SyncAssetsIfRequestedAsync(
    CommandLineOptions options,
    ILogger logger
  )
  {
    if (!options.SyncAssets)
    {
      return 0;
    }

    return await AssetSyncCommand.ExecuteAsync(
      options.MTGOAssets,
      options.R2,
      options.SourceManifestRoot,
      logger
    );
  }

  public static async Task<ImportPreflightResult> CheckPreflightAsync(
    string dataDirectory,
    string connectionString,
    CommandLineOptions options,
    ILoggerFactory loggerFactory,
    ILogger logger
  )
  {
    var parser = new Parser(
      dataDirectory,
      loggerFactory.CreateLogger<Parser>()
    );

    SourceManifestComparison cardDataComparison = SourceManifestPreflight.CompareCardData(
      options.SourceManifestRoot,
      parser.EnumerateParsedSourceFiles()
    );
    if (cardDataComparison.HasChanges)
    {
      logger.LogInformation(
        "Card data source manifest changed; full card data import will continue: {Reason}.",
        cardDataComparison.Reason
      );
      return ImportPreflightResult.Changed(ImportPipelineAction.ImportCards);
    }

    SourceManifestComparison legalityComparison = SourceManifestPreflight.CompareLegalities(
      options.SourceManifestRoot,
      parser.EnumerateLegalitySourceFiles()
    );
    if (legalityComparison.HasChanges)
    {
      logger.LogInformation(
        "Legality source manifest changed; legality-only import will continue: {Reason}.",
        legalityComparison.Reason
      );
      return ImportPreflightResult.Changed(ImportPipelineAction.ImportLegalities);
    }

    ImportDatabaseState databaseState = await ImportDatabaseState.ReadAsync(connectionString);
    if (!databaseState.HasCardData)
    {
      logger.LogInformation(
        "Database is missing imported card data; full card data import will continue. Current counts: {SetCount} sets, {CardCount} cards, {ProductCount} products, {CardCatalogVariantCount} card catalog variants, {OracleCardCount} oracle cards, {CardFaceCount} card faces.",
        databaseState.SetCount,
        databaseState.CardCount,
        databaseState.ProductCount,
        databaseState.CardCatalogVariantCount,
        databaseState.OracleCardCount,
        databaseState.CardFaceCount
      );
      return ImportPreflightResult.Changed(ImportPipelineAction.ImportCards);
    }

    SourceManifestImportCounts expectedCounts = cardDataComparison.ImportCounts!;
    if (CardDataCountsChanged(expectedCounts, databaseState))
    {
      logger.LogInformation(
        "Database card data counts differ from the source manifest; full card data import will continue. Expected {ExpectedSetCount} sets, {ExpectedCardCount} cards, {ExpectedProductCount} products, {ExpectedCardCatalogVariantCount} card catalog variants, {ExpectedOracleCardCount} oracle cards, {ExpectedCardFaceCount} card faces; found {SetCount} sets, {CardCount} cards, {ProductCount} products, {CardCatalogVariantCount} card catalog variants, {OracleCardCount} oracle cards, {CardFaceCount} card faces.",
        expectedCounts.SetCount,
        expectedCounts.CardCount,
        expectedCounts.ProductCount,
        expectedCounts.CardCatalogVariantCount,
        expectedCounts.OracleCardCount,
        expectedCounts.CardFaceCount,
        databaseState.SetCount,
        databaseState.CardCount,
        databaseState.ProductCount,
        databaseState.CardCatalogVariantCount,
        databaseState.OracleCardCount,
        databaseState.CardFaceCount
      );
      return ImportPreflightResult.Changed(ImportPipelineAction.ImportCards);
    }

    if (!databaseState.HasLegalities)
    {
      logger.LogInformation(
        "Database is missing imported legalities; legality-only import will continue. Current legality count: {LegalityCount}.",
        databaseState.LegalityCount
      );
      return ImportPreflightResult.Changed(ImportPipelineAction.ImportLegalities);
    }

    if (databaseState.LegalityCount != expectedCounts.LegalityCount)
    {
      logger.LogInformation(
        "Database legality count differs from the source manifest; legality-only import will continue. Expected {ExpectedLegalityCount}; found {LegalityCount}.",
        expectedCounts.LegalityCount,
        databaseState.LegalityCount
      );
      return ImportPreflightResult.Changed(ImportPipelineAction.ImportLegalities);
    }

    logger.LogInformation(
      "No source file changes detected across {CardSourceFileCount} card data files and {LegalitySourceFileCount} legality files in {ManifestPath}; skipping import.",
      cardDataComparison.SourceFileCount,
      legalityComparison.SourceFileCount,
      cardDataComparison.ManifestPath
    );
    return ImportPreflightResult.Skip();
  }

  private static bool CardDataCountsChanged(
    SourceManifestImportCounts expectedCounts,
    ImportDatabaseState databaseState
  )
  {
    return databaseState.SetCount != expectedCounts.SetCount ||
      databaseState.CardCount != expectedCounts.CardCount ||
      databaseState.ProductCount != expectedCounts.ProductCount ||
      databaseState.CardCatalogVariantCount != expectedCounts.CardCatalogVariantCount ||
      databaseState.OracleCardCount != expectedCounts.OracleCardCount ||
      databaseState.CardFaceCount != expectedCounts.CardFaceCount;
  }
}

internal enum ImportPipelineAction
{
  Skip,
  ImportCards,
  ImportLegalities
}

internal sealed record ImportPreflightResult(
  ImportPipelineAction Action
)
{
  public bool ShouldSkip => Action == ImportPipelineAction.Skip;

  public static ImportPreflightResult Skip()
  {
    return new ImportPreflightResult(ImportPipelineAction.Skip);
  }

  public static ImportPreflightResult Changed(ImportPipelineAction action)
  {
    return new ImportPreflightResult(action);
  }
}
