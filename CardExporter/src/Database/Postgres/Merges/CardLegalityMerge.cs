/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Threading.Tasks;
using Npgsql;


namespace CardExporter.Database.Postgres;

internal static class CardLegalityMerge
{
  public static async Task MergeAsync(NpgsqlConnection connection)
  {
    await using var command = new NpgsqlCommand(
      """
      INSERT INTO card_legalities (
        oracle_id,
        format_code,
        status,
        source_rule_set_id,
        last_seen_at
      )
      SELECT DISTINCT ON (oracle_id, format_code)
        l.oracle_id,
        l.format_code,
        l.status,
        l.source_rule_set_id,
        now()
      FROM tmp_card_legalities l
      JOIN oracle_cards o ON o.id = l.oracle_id
      JOIN formats f ON f.code = l.format_code
      ORDER BY
        l.oracle_id,
        l.format_code,
        CASE l.status
          WHEN 'banned' THEN 4
          WHEN 'restricted' THEN 3
          WHEN 'suspended' THEN 2
          WHEN 'legal' THEN 1
          ELSE 0
        END DESC
      ON CONFLICT (oracle_id, format_code) DO UPDATE SET
        status = EXCLUDED.status,
        source_rule_set_id = EXCLUDED.source_rule_set_id,
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
      DELETE FROM card_legalities l
      WHERE NOT EXISTS (
        SELECT 1
        FROM tmp_card_legalities t
        WHERE t.oracle_id = l.oracle_id
          AND t.format_code = l.format_code
      );
      """,
      connection
    );

    return await command.ExecuteNonQueryAsync();
  }
}
