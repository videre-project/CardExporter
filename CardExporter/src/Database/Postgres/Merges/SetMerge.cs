/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Threading.Tasks;
using Npgsql;


namespace CardExporter.Database.Postgres;

internal static class SetMerge
{
  public static async Task MergeAsync(NpgsqlConnection connection)
  {
    await using var command = new NpgsqlCommand(
      """
      INSERT INTO sets (code, name, release_date, age, set_type, raw, last_seen_at)
      SELECT DISTINCT ON (code)
        s.code,
        NULLIF(s.name, ''),
        s.release_date,
        s.age,
        s.set_type,
        s.raw,
        now()
      FROM tmp_sets s
      ORDER BY s.code
      ON CONFLICT (code) DO UPDATE SET
        name = EXCLUDED.name,
        release_date = EXCLUDED.release_date,
        age = EXCLUDED.age,
        set_type = EXCLUDED.set_type,
        raw = EXCLUDED.raw,
        last_seen_at = now();
      """,
      connection
    );

    await command.ExecuteNonQueryAsync();
  }

  public static async Task MergeReferencedCodesAsync(NpgsqlConnection connection)
  {
    await using var command = new NpgsqlCommand(
      """
      INSERT INTO sets (code, last_seen_at)
      SELECT DISTINCT
        set_code,
        now()
      FROM tmp_cards
      WHERE set_code IS NOT NULL
      UNION
      SELECT DISTINCT
        set_code,
        now()
      FROM tmp_products
      WHERE set_code IS NOT NULL
      ON CONFLICT (code) DO NOTHING;
      """,
      connection
    );

    await command.ExecuteNonQueryAsync();
  }

  public static async Task<long> DeleteStaleAsync(NpgsqlConnection connection)
  {
    await using var command = new NpgsqlCommand(
      """
      DELETE FROM sets s
      WHERE NOT EXISTS (
        SELECT 1
        FROM tmp_sets t
        WHERE t.code = s.code
      )
      AND NOT EXISTS (
        SELECT 1
        FROM cards c
        WHERE c.set_code = s.code
      )
      AND NOT EXISTS (
        SELECT 1
        FROM products p
        WHERE p.set_code = s.code
      );
      """,
      connection
    );

    return await command.ExecuteNonQueryAsync();
  }
}
