/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Threading.Tasks;
using Npgsql;


namespace CardExporter.Database.Postgres;

internal static class CatalogItemMerge
{
  public static async Task<long> RefreshAsync(NpgsqlConnection connection)
  {
    await using var command = new NpgsqlCommand(
      """
      WITH catalog_union AS (
        SELECT id AS catalog_id, 'card' AS kind, 3 AS priority FROM cards
        UNION ALL
        SELECT catalog_id, 'card_variant' AS kind, 2 AS priority FROM card_catalog_variants
        UNION ALL
        SELECT id AS catalog_id, 'product' AS kind, 1 AS priority FROM products
      ),
      catalog_ranked AS (
        SELECT DISTINCT ON (catalog_id) catalog_id, kind
        FROM catalog_union
        ORDER BY catalog_id, priority
      )
      INSERT INTO catalog_items (catalog_id, kind, last_seen_at)
      SELECT catalog_id, kind, now()
      FROM catalog_ranked
      ON CONFLICT (catalog_id) DO UPDATE SET
        kind = EXCLUDED.kind,
        last_seen_at = now();
      """,
      connection
    );

    return await command.ExecuteNonQueryAsync();
  }
}
