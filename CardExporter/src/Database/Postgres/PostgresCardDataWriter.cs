/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Threading.Tasks;
using CardExporter.MTGO;
using Microsoft.Extensions.Logging;
using Npgsql;


namespace CardExporter.Database.Postgres;

internal sealed class PostgresCardDataWriter
{
  private readonly string _connectionString;
  private readonly ILogger _logger;

  public PostgresCardDataWriter(string connectionString, ILogger logger)
  {
    _connectionString = connectionString;
    _logger = logger;
  }

  public async Task<CardDataImportResult> ImportAsync(Parser parser)
  {
    await using var connection = new NpgsqlConnection(_connectionString);
    await connection.OpenAsync();
    await ImportSchema.EnsureCurrentSchemaAsync(connection);
    await using var transaction = await connection.BeginTransactionAsync();

    await ImportSchema.CreateStagingTablesAsync(connection);

    StagedImportCounts stagedCounts = await StagingCopyWriter.CopyAsync(connection, parser);
    CardImageChangeSet imageChanges = await CardImageChangeTracker.GetChangedImageCatalogIdsAsync(connection);
    await PendingImageSyncRepository.AddAsync(
      connection,
      imageChanges.AddedCatalogIds,
      PendingImageSyncReasons.Added
    );
    await PendingImageSyncRepository.AddAsync(
      connection,
      imageChanges.ModifiedCatalogIds,
      PendingImageSyncReasons.Modified
    );
    await StagingMerger.MergeAsync(connection);
    StaleDeleteCounts deletedCounts = await StagingMerger.DeleteStaleAsync(connection);
    ImportDatabaseState finalDatabaseState = await ImportDatabaseState.ReadAsync(connection);

    await transaction.CommitAsync();
    LogCompletedImport(stagedCounts, deletedCounts, imageChanges);
    return new CardDataImportResult(stagedCounts, deletedCounts, imageChanges, finalDatabaseState);
  }

  public async Task<ImportDatabaseState> ImportLegalitiesAsync(Parser parser)
  {
    await using var connection = new NpgsqlConnection(_connectionString);
    await connection.OpenAsync();
    await ImportSchema.EnsureCurrentSchemaAsync(connection);
    await using var transaction = await connection.BeginTransactionAsync();

    await ImportSchema.CreateLegalityStagingTableAsync(connection);

    long legalityCount = await CardLegalityCopy.WriteAsync(connection, parser.EnumerateCardLegalities());
    await CardLegalityMerge.MergeAsync(connection);
    long deletedLegalityCount = await CardLegalityMerge.DeleteStaleAsync(connection);
    ImportDatabaseState finalDatabaseState = await ImportDatabaseState.ReadAsync(connection);

    await transaction.CommitAsync();
    _logger.LogInformation(
      "Refreshed {LegalityCount} card legalities; {DeletedLegalityCount} stale legalities deleted.",
      legalityCount,
      deletedLegalityCount
    );
    return finalDatabaseState;
  }

  private void LogCompletedImport(
    StagedImportCounts stagedCounts,
    StaleDeleteCounts deletedCounts,
    CardImageChangeSet imageChanges
  )
  {
    _logger.LogInformation(
      "Imported {SetCount} sets, {CardCount} cards, {ProductCount} products, {CardCatalogVariantCount} card catalog variants, {FaceCount} card faces, and {LegalityCount} card legalities; {AddedImageCatalogIdCount} added image catalog IDs and {ModifiedImageCatalogIdCount} modified image catalog IDs detected; {DeletedLegalityCount} stale legalities deleted, {DeletedFaceCount} stale faces deleted, {DeletedCardCount} stale cards deleted, {DeletedProductCount} stale products deleted, {DeletedCardCatalogVariantCount} stale card catalog variants deleted, {DeletedOracleCardCount} stale oracle cards deleted, {DeletedSetCount} stale sets deleted.",
      stagedCounts.SetCount,
      stagedCounts.CardCount,
      stagedCounts.ProductCount,
      stagedCounts.CardCatalogVariantCount,
      stagedCounts.FaceCount,
      stagedCounts.LegalityCount,
      imageChanges.AddedCatalogIds.Count,
      imageChanges.ModifiedCatalogIds.Count,
      deletedCounts.LegalityCount,
      deletedCounts.FaceCount,
      deletedCounts.CardCount,
      deletedCounts.ProductCount,
      deletedCounts.CardCatalogVariantCount,
      deletedCounts.OracleCardCount,
      deletedCounts.SetCount
    );
  }
}
