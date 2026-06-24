/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Threading.Tasks;
using Npgsql;


namespace CardExporter.Database.Postgres;

internal static class ProductMerge
{
  public static async Task MergeAsync(NpgsqlConnection connection)
  {
    await using var command = new NpgsqlCommand(
      """
      INSERT INTO products (
        id,
        set_code,
        name,
        object_type,
        texture_number,
        is_tradable,
        raw,
        last_seen_at
      )
      SELECT DISTINCT ON (id)
        p.id,
        p.set_code,
        NULLIF(p.name, ''),
        NULLIF(p.object_type, ''),
        p.texture_number,
        p.is_tradable,
        p.raw,
        now()
      FROM tmp_products p
      ORDER BY
        p.id,
        p.name IS NULL,
        coalesce(length(p.name), 0) DESC
      ON CONFLICT (id) DO UPDATE SET
        set_code = EXCLUDED.set_code,
        name = EXCLUDED.name,
        object_type = EXCLUDED.object_type,
        texture_number = EXCLUDED.texture_number,
        is_tradable = EXCLUDED.is_tradable,
        raw = EXCLUDED.raw,
        last_seen_at = now();
      """,
      connection
    );

    await command.ExecuteNonQueryAsync();
  }

  public static async Task<long> DeleteStaleAsync(NpgsqlConnection connection)
  {
    await using var command = new NpgsqlCommand(
      """
      DELETE FROM products p
      WHERE NOT EXISTS (
        SELECT 1
        FROM tmp_products t
        WHERE t.id = p.id
      );
      """,
      connection
    );

    return await command.ExecuteNonQueryAsync();
  }
}
