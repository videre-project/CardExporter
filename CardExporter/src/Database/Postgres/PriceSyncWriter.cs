/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CardExporter.Prices;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;


namespace CardExporter.Database.Postgres;

internal sealed class PriceSyncWriter
{
  private readonly string _connectionString;
  private readonly ILogger? _logger;
  private const int UnknownCatalogIdLogLimit = 20;

  public PriceSyncWriter(string connectionString, ILogger? logger = null)
  {
    _connectionString = connectionString;
    _logger = logger;
  }

  public async Task<PriceDefinitionImportResult> ImportDefinitionsAsync(
    IEnumerable<CatalogPriceDefinitionRecord> definitions,
    bool dryRun
  )
  {
    await using var connection = new NpgsqlConnection(_connectionString);
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync();

    await CatalogItemMerge.RefreshAsync(connection);
    await CreateDefinitionStagingTableAsync(connection);
    long stagedCount = await CopyDefinitionsAsync(connection, definitions);
    long unknownCatalogIdCount = await CountUnknownDefinitionCatalogIdsAsync(connection);
    await LogUnknownDefinitionCatalogIdsAsync(connection, unknownCatalogIdCount);
    long matchedCount = await CountMatchedDefinitionsAsync(connection);
    long importedCount = dryRun ? matchedCount : await MergeDefinitionsAsync(connection);

    if (dryRun)
    {
      await transaction.RollbackAsync();
    }
    else
    {
      await transaction.CommitAsync();
    }

    return new PriceDefinitionImportResult(stagedCount, importedCount, unknownCatalogIdCount);
  }

  public async Task<PriceHistoryImportResult> ImportPricesAsync(
    IEnumerable<CatalogPriceRecord> prices,
    bool dryRun
  )
  {
    await using var connection = new NpgsqlConnection(_connectionString);
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync();

    await CatalogItemMerge.RefreshAsync(connection);
    await CreatePriceStagingTableAsync(connection);
    long stagedCount = await CopyPricesAsync(connection, prices);
    long unknownCatalogIdCount = await CountUnknownPriceCatalogIdsAsync(connection);
    await LogUnknownPriceCatalogIdsAsync(connection, unknownCatalogIdCount);
    long matchedCount = await CountMatchedPricesAsync(connection);
    long importedCount = dryRun ? matchedCount : await MergePricesAsync(connection);

    if (dryRun)
    {
      await transaction.RollbackAsync();
    }
    else
    {
      await transaction.CommitAsync();
    }

    return new PriceHistoryImportResult(stagedCount, importedCount, unknownCatalogIdCount);
  }

  private static async Task CreateDefinitionStagingTableAsync(NpgsqlConnection connection)
  {
    await using var command = new NpgsqlCommand(
      """
      CREATE TEMP TABLE tmp_catalog_price_definitions (
        source TEXT NOT NULL,
        catalog_id INTEGER NOT NULL,
        source_name TEXT NULL,
        source_cardset TEXT NULL,
        source_rarity TEXT NULL,
        source_version TEXT NULL,
        source_foil BOOLEAN NULL,
        raw JSONB NOT NULL
      ) ON COMMIT DROP;
      """,
      connection
    );

    await command.ExecuteNonQueryAsync();
  }

  private static async Task CreatePriceStagingTableAsync(NpgsqlConnection connection)
  {
    await using var command = new NpgsqlCommand(
      """
      CREATE TEMP TABLE tmp_catalog_price_history (
        source TEXT NOT NULL,
        price_date DATE NOT NULL,
        catalog_id INTEGER NOT NULL,
        sell_price NUMERIC NOT NULL
      ) ON COMMIT DROP;
      """,
      connection
    );

    await command.ExecuteNonQueryAsync();
  }

  private static async Task<long> CopyDefinitionsAsync(
    NpgsqlConnection connection,
    IEnumerable<CatalogPriceDefinitionRecord> definitions
  )
  {
    long count = 0;
    await using var importer = await connection.BeginBinaryImportAsync(
      """
      COPY tmp_catalog_price_definitions (
        source,
        catalog_id,
        source_name,
        source_cardset,
        source_rarity,
        source_version,
        source_foil,
        raw
      ) FROM STDIN (FORMAT BINARY)
      """
    );

    foreach (CatalogPriceDefinitionRecord definition in definitions)
    {
      await importer.StartRowAsync();
      await importer.WriteAsync(definition.Source, NpgsqlDbType.Text);
      await importer.WriteAsync(definition.CatalogId, NpgsqlDbType.Integer);
      await importer.WriteAsync(definition.SourceName, NpgsqlDbType.Text);
      await importer.WriteAsync(definition.SourceCardset, NpgsqlDbType.Text);
      await importer.WriteAsync(definition.SourceRarity, NpgsqlDbType.Text);
      await importer.WriteAsync(definition.SourceVersion, NpgsqlDbType.Text);
      await importer.WriteAsync(definition.SourceFoil, NpgsqlDbType.Boolean);
      await importer.WriteAsync(definition.RawJson, NpgsqlDbType.Jsonb);
      count++;
    }

    await importer.CompleteAsync();
    return count;
  }

  private static async Task<long> CopyPricesAsync(
    NpgsqlConnection connection,
    IEnumerable<CatalogPriceRecord> prices
  )
  {
    long count = 0;
    await using var importer = await connection.BeginBinaryImportAsync(
      """
      COPY tmp_catalog_price_history (
        source,
        price_date,
        catalog_id,
        sell_price
      ) FROM STDIN (FORMAT BINARY)
      """
    );

    foreach (CatalogPriceRecord price in prices)
    {
      await importer.StartRowAsync();
      await importer.WriteAsync(price.Source, NpgsqlDbType.Text);
      await importer.WriteAsync(price.PriceDate, NpgsqlDbType.Date);
      await importer.WriteAsync(price.CatalogId, NpgsqlDbType.Integer);
      await importer.WriteAsync(price.SellPrice, NpgsqlDbType.Numeric);
      count++;
    }

    await importer.CompleteAsync();
    return count;
  }

  private static async Task<long> CountUnknownDefinitionCatalogIdsAsync(NpgsqlConnection connection)
  {
    await using var command = new NpgsqlCommand(
      """
      SELECT count(DISTINCT d.catalog_id)
      FROM tmp_catalog_price_definitions d
      WHERE NOT EXISTS (
        SELECT 1 FROM catalog_items ci WHERE ci.catalog_id = d.catalog_id
      );
      """,
      connection
    );

    return Convert.ToInt64(await command.ExecuteScalarAsync());
  }

  private static async Task<long> CountUnknownPriceCatalogIdsAsync(NpgsqlConnection connection)
  {
    await using var command = new NpgsqlCommand(
      """
      SELECT count(DISTINCT p.catalog_id)
      FROM tmp_catalog_price_history p
      WHERE NOT EXISTS (
        SELECT 1 FROM catalog_items ci WHERE ci.catalog_id = p.catalog_id
      );
      """,
      connection
    );

    return Convert.ToInt64(await command.ExecuteScalarAsync());
  }

  private static async Task<long> CountMatchedDefinitionsAsync(NpgsqlConnection connection)
  {
    await using var command = new NpgsqlCommand(
      """
      SELECT count(*)
      FROM (
        SELECT DISTINCT d.source, d.catalog_id
        FROM tmp_catalog_price_definitions d
        WHERE EXISTS (
          SELECT 1 FROM catalog_items ci WHERE ci.catalog_id = d.catalog_id
        )
      ) matched_definitions;
      """,
      connection
    );

    return Convert.ToInt64(await command.ExecuteScalarAsync());
  }

  private static async Task<long> CountMatchedPricesAsync(NpgsqlConnection connection)
  {
    await using var command = new NpgsqlCommand(
      """
      SELECT count(*)
      FROM (
        SELECT DISTINCT p.source, p.price_date, p.catalog_id
        FROM tmp_catalog_price_history p
        WHERE EXISTS (
          SELECT 1 FROM catalog_items ci WHERE ci.catalog_id = p.catalog_id
        )
      ) matched_prices;
      """,
      connection
    );

    return Convert.ToInt64(await command.ExecuteScalarAsync());
  }

  private async Task LogUnknownDefinitionCatalogIdsAsync(
    NpgsqlConnection connection,
    long unknownCatalogIdCount
  )
  {
    if (_logger is null || unknownCatalogIdCount == 0)
    {
      return;
    }

    IReadOnlyList<int> unknownCatalogIds = await ReadUnknownDefinitionCatalogIdsAsync(connection);
    _logger.LogWarning(
      "Skipping {UnknownDefinitionCatalogIdCount} GoatBots definition catalog IDs not found in catalog_items. Sample: {UnknownDefinitionCatalogIds}",
      unknownCatalogIdCount,
      string.Join(", ", unknownCatalogIds)
    );
  }

  private async Task LogUnknownPriceCatalogIdsAsync(
    NpgsqlConnection connection,
    long unknownCatalogIdCount
  )
  {
    if (_logger is null || unknownCatalogIdCount == 0)
    {
      return;
    }

    IReadOnlyList<int> unknownCatalogIds = await ReadUnknownPriceCatalogIdsAsync(connection);
    _logger.LogWarning(
      "Skipping {UnknownPriceCatalogIdCount} GoatBots price catalog IDs not found in catalog_items. Sample: {UnknownPriceCatalogIds}",
      unknownCatalogIdCount,
      string.Join(", ", unknownCatalogIds)
    );
  }

  private static async Task<IReadOnlyList<int>> ReadUnknownDefinitionCatalogIdsAsync(NpgsqlConnection connection)
  {
    await using var command = new NpgsqlCommand(
      """
      SELECT DISTINCT d.catalog_id
      FROM tmp_catalog_price_definitions d
      WHERE NOT EXISTS (
        SELECT 1 FROM catalog_items ci WHERE ci.catalog_id = d.catalog_id
      )
      ORDER BY d.catalog_id
      LIMIT @limit;
      """,
      connection
    );
    command.Parameters.AddWithValue("limit", UnknownCatalogIdLogLimit);

    return await ReadCatalogIdsAsync(command);
  }

  private static async Task<IReadOnlyList<int>> ReadUnknownPriceCatalogIdsAsync(NpgsqlConnection connection)
  {
    await using var command = new NpgsqlCommand(
      """
      SELECT DISTINCT p.catalog_id
      FROM tmp_catalog_price_history p
      WHERE NOT EXISTS (
        SELECT 1 FROM catalog_items ci WHERE ci.catalog_id = p.catalog_id
      )
      ORDER BY p.catalog_id
      LIMIT @limit;
      """,
      connection
    );
    command.Parameters.AddWithValue("limit", UnknownCatalogIdLogLimit);

    return await ReadCatalogIdsAsync(command);
  }

  private static async Task<IReadOnlyList<int>> ReadCatalogIdsAsync(NpgsqlCommand command)
  {
    var catalogIds = new List<int>();
    await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
      catalogIds.Add(reader.GetInt32(0));
    }

    return catalogIds;
  }

  private static async Task<long> MergeDefinitionsAsync(NpgsqlConnection connection)
  {
    await using var command = new NpgsqlCommand(
      """
      INSERT INTO catalog_price_definitions (
        source,
        catalog_id,
        source_name,
        source_cardset,
        source_rarity,
        source_version,
        source_foil,
        raw,
        last_seen_at
      )
      SELECT DISTINCT ON (d.source, d.catalog_id)
        d.source,
        d.catalog_id,
        NULLIF(d.source_name, ''),
        NULLIF(d.source_cardset, ''),
        NULLIF(d.source_rarity, ''),
        NULLIF(d.source_version, ''),
        d.source_foil,
        d.raw,
        now()
      FROM tmp_catalog_price_definitions d
      INNER JOIN catalog_items ci ON ci.catalog_id = d.catalog_id
      ORDER BY d.source, d.catalog_id
      ON CONFLICT (source, catalog_id) DO UPDATE SET
        source_name = EXCLUDED.source_name,
        source_cardset = EXCLUDED.source_cardset,
        source_rarity = EXCLUDED.source_rarity,
        source_version = EXCLUDED.source_version,
        source_foil = EXCLUDED.source_foil,
        raw = EXCLUDED.raw,
        last_seen_at = now();
      """,
      connection
    );

    return await command.ExecuteNonQueryAsync();
  }

  private static async Task<long> MergePricesAsync(NpgsqlConnection connection)
  {
    await using var command = new NpgsqlCommand(
      """
      INSERT INTO catalog_price_history (
        source,
        price_date,
        catalog_id,
        sell_price,
        last_seen_at
      )
      SELECT DISTINCT ON (p.source, p.price_date, p.catalog_id)
        p.source,
        p.price_date,
        p.catalog_id,
        p.sell_price,
        now()
      FROM tmp_catalog_price_history p
      INNER JOIN catalog_items ci ON ci.catalog_id = p.catalog_id
      ORDER BY p.source, p.price_date, p.catalog_id
      ON CONFLICT (source, price_date, catalog_id) DO UPDATE SET
        sell_price = EXCLUDED.sell_price,
        last_seen_at = now();
      """,
      connection
    );

    return await command.ExecuteNonQueryAsync();
  }
}
