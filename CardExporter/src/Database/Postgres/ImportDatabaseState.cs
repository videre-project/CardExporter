/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Threading.Tasks;
using Npgsql;


namespace CardExporter.Database.Postgres;

internal sealed record ImportDatabaseState(
  long SetCount,
  long CardCount,
  long ProductCount,
  long CardCatalogVariantCount,
  long OracleCardCount,
  long CardFaceCount,
  long LegalityCount
)
{
  public bool HasCardData => SetCount > 0 && CardCount > 0 && ProductCount > 0 && OracleCardCount > 0;

  public bool HasLegalities => LegalityCount > 0;

  public static async Task<ImportDatabaseState> ReadAsync(string connectionString)
  {
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();

    return new ImportDatabaseState(
      await CountRowsIfTableExistsAsync(connection, "sets"),
      await CountRowsIfTableExistsAsync(connection, "cards"),
      await CountRowsIfTableExistsAsync(connection, "products"),
      await CountRowsIfTableExistsAsync(connection, "card_catalog_variants"),
      await CountRowsIfTableExistsAsync(connection, "oracle_cards"),
      await CountRowsIfTableExistsAsync(connection, "card_faces"),
      await CountRowsIfTableExistsAsync(connection, "card_legalities")
    );
  }

  public static async Task<ImportDatabaseState> ReadAsync(NpgsqlConnection connection)
  {
    return new ImportDatabaseState(
      await CountRowsIfTableExistsAsync(connection, "sets"),
      await CountRowsIfTableExistsAsync(connection, "cards"),
      await CountRowsIfTableExistsAsync(connection, "products"),
      await CountRowsIfTableExistsAsync(connection, "card_catalog_variants"),
      await CountRowsIfTableExistsAsync(connection, "oracle_cards"),
      await CountRowsIfTableExistsAsync(connection, "card_faces"),
      await CountRowsIfTableExistsAsync(connection, "card_legalities")
    );
  }

  private static async Task<long> CountRowsIfTableExistsAsync(
    NpgsqlConnection connection,
    string tableName
  )
  {
    if (!await TableExistsAsync(connection, tableName))
    {
      return 0;
    }

    await using var countCommand = new NpgsqlCommand(
      $"SELECT count(*) FROM {tableName};",
      connection
    );
    return (long)(await countCommand.ExecuteScalarAsync() ?? 0L);
  }

  private static async Task<bool> TableExistsAsync(
    NpgsqlConnection connection,
    string tableName
  )
  {
    await using var command = new NpgsqlCommand(
      "SELECT to_regclass(@tableName) IS NOT NULL;",
      connection
    );
    command.Parameters.AddWithValue("tableName", $"public.{tableName}");
    return (bool)(await command.ExecuteScalarAsync() ?? false);
  }
}
