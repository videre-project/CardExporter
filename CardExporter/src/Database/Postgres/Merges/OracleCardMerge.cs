/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Threading.Tasks;
using Npgsql;


namespace CardExporter.Database.Postgres;

internal static class OracleCardMerge
{
  public static async Task MergeAsync(NpgsqlConnection connection)
  {
    await using var command = new NpgsqlCommand(
      """
      INSERT INTO oracle_cards (
        id,
        name,
        mana_cost,
        mana_value,
        type_line,
        oracle_text,
        colors,
        color_identity,
        color_mask,
        color_identity_mask,
        supertypes,
        card_types,
        subtypes,
        power,
        toughness,
        loyalty,
        defense,
        is_token,
        last_seen_at
      )
      SELECT DISTINCT ON (oracle_id)
        oracle_id,
        NULLIF(name, ''),
        mana_cost,
        mana_value,
        type_line,
        oracle_text,
        colors,
        color_identity,
        color_mask,
        color_identity_mask,
        supertypes,
        card_types,
        subtypes,
        power,
        toughness,
        loyalty,
        defense,
        is_token,
        now()
      FROM tmp_cards
      ORDER BY
        oracle_id,
        name IS NULL,
        name LIKE 'MA_CTOKEN_%',
        coalesce(length(name), 0) DESC
      ON CONFLICT (id) DO UPDATE SET
        name = EXCLUDED.name,
        mana_cost = EXCLUDED.mana_cost,
        mana_value = EXCLUDED.mana_value,
        type_line = EXCLUDED.type_line,
        oracle_text = EXCLUDED.oracle_text,
        colors = EXCLUDED.colors,
        color_identity = EXCLUDED.color_identity,
        color_mask = EXCLUDED.color_mask,
        color_identity_mask = EXCLUDED.color_identity_mask,
        supertypes = EXCLUDED.supertypes,
        card_types = EXCLUDED.card_types,
        subtypes = EXCLUDED.subtypes,
        power = EXCLUDED.power,
        toughness = EXCLUDED.toughness,
        loyalty = EXCLUDED.loyalty,
        defense = EXCLUDED.defense,
        is_token = EXCLUDED.is_token,
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
      DELETE FROM oracle_cards o
      WHERE NOT EXISTS (
        SELECT 1
        FROM cards c
        WHERE c.oracle_id = o.id
      );
      """,
      connection
    );

    return await command.ExecuteNonQueryAsync();
  }
}
