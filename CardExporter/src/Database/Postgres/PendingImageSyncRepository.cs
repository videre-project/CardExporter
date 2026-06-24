/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Npgsql;


namespace CardExporter.Database.Postgres;

internal sealed record PendingImageSyncEntry(int CatalogId, string? Reason);

internal static class PendingImageSyncReasons
{
  public const string Added = "card-data-import-added";
  public const string Modified = "card-data-import-modified";

  public static bool IsAdded(string? reason)
  {
    return string.Equals(reason, Added, System.StringComparison.Ordinal);
  }
}

internal static class PendingImageSyncRepository
{
  public static async Task EnsureSchemaAsync(NpgsqlConnection connection)
  {
    await using var command = new NpgsqlCommand(
      """
      CREATE TABLE IF NOT EXISTS pending_image_sync (
        catalog_id INTEGER PRIMARY KEY,
        reason TEXT NULL,
        first_seen_at TIMESTAMPTZ NOT NULL DEFAULT now(),
        last_seen_at TIMESTAMPTZ NOT NULL DEFAULT now()
      );
      """,
      connection
    );

    await command.ExecuteNonQueryAsync();
  }

  public static async Task AddAsync(
    NpgsqlConnection connection,
    IReadOnlySet<int> catalogIds,
    string reason
  )
  {
    if (catalogIds.Count == 0)
    {
      return;
    }

    await using var command = new NpgsqlCommand(
      """
      INSERT INTO pending_image_sync (catalog_id, reason, last_seen_at)
      SELECT DISTINCT catalog_id, @reason, now()
      FROM unnest(@catalogIds) AS pending(catalog_id)
      ON CONFLICT (catalog_id) DO UPDATE SET
        reason = EXCLUDED.reason,
        last_seen_at = now();
      """,
      connection
    );
    command.Parameters.AddWithValue("catalogIds", catalogIds.ToArray());
    command.Parameters.AddWithValue("reason", reason);
    await command.ExecuteNonQueryAsync();
  }

  public static async Task<IReadOnlyList<PendingImageSyncEntry>> GetEntriesAsync(
    string connectionString,
    bool createIfMissing = true
  )
  {
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    if (createIfMissing)
    {
      await EnsureSchemaAsync(connection);
    }
    else if (!await TableExistsAsync(connection))
    {
      return [];
    }

    await using var command = new NpgsqlCommand(
      """
      SELECT catalog_id, reason
      FROM pending_image_sync
      ORDER BY catalog_id;
      """,
      connection
    );

    List<PendingImageSyncEntry> entries = new List<PendingImageSyncEntry>();
    await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
      entries.Add(
        new PendingImageSyncEntry(
          reader.GetInt32(0),
          reader.IsDBNull(1) ? null : reader.GetString(1)
        )
      );
    }

    return entries;
  }

  private static async Task<bool> TableExistsAsync(NpgsqlConnection connection)
  {
    await using var command = new NpgsqlCommand(
      "SELECT to_regclass('public.pending_image_sync') IS NOT NULL;",
      connection
    );
    return (bool)(await command.ExecuteScalarAsync() ?? false);
  }

  public static async Task<int> ClearAsync(
    string connectionString,
    IReadOnlyCollection<int> catalogIds
  )
  {
    if (catalogIds.Count == 0)
    {
      return 0;
    }

    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await EnsureSchemaAsync(connection);

    await using var command = new NpgsqlCommand(
      """
      DELETE FROM pending_image_sync
      WHERE catalog_id = ANY(@catalogIds);
      """,
      connection
    );
    command.Parameters.AddWithValue("catalogIds", catalogIds.ToArray());
    return await command.ExecuteNonQueryAsync();
  }
}
