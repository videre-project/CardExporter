/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Threading.Tasks;
using Npgsql;


namespace CardExporter.Database.Postgres;

internal static class CardFaceMerge
{
  public static async Task MergeAsync(NpgsqlConnection connection)
  {
    await using var command = new NpgsqlCommand(
      """
      INSERT INTO card_faces (
        card_id,
        face_index,
        source_catalog_id,
        name,
        colors,
        color_mask,
        mana_value,
        flavor_text,
        mana_cost,
        type_line,
        oracle_text,
        supertypes,
        card_types,
        subtypes,
        power,
        toughness,
        loyalty,
        defense,
        artist,
        art_id,
        raw
      )
      SELECT DISTINCT ON (card_id, face_index)
        card_id,
        face_index,
        source_catalog_id,
        NULLIF(name, ''),
        colors,
        color_mask,
        mana_value,
        flavor_text,
        mana_cost,
        type_line,
        oracle_text,
        supertypes,
        card_types,
        subtypes,
        power,
        toughness,
        loyalty,
        defense,
        artist,
        art_id,
        raw
      FROM tmp_card_faces
      ORDER BY
        card_id,
        face_index,
        name IS NULL,
        coalesce(length(name), 0) DESC
      ON CONFLICT (card_id, face_index) DO UPDATE SET
        source_catalog_id = EXCLUDED.source_catalog_id,
        name = EXCLUDED.name,
        colors = EXCLUDED.colors,
        color_mask = EXCLUDED.color_mask,
        mana_value = EXCLUDED.mana_value,
        flavor_text = EXCLUDED.flavor_text,
        mana_cost = EXCLUDED.mana_cost,
        type_line = EXCLUDED.type_line,
        oracle_text = EXCLUDED.oracle_text,
        supertypes = EXCLUDED.supertypes,
        card_types = EXCLUDED.card_types,
        subtypes = EXCLUDED.subtypes,
        power = EXCLUDED.power,
        toughness = EXCLUDED.toughness,
        loyalty = EXCLUDED.loyalty,
        defense = EXCLUDED.defense,
        artist = EXCLUDED.artist,
        art_id = EXCLUDED.art_id,
        raw = EXCLUDED.raw;
      """,
      connection
    );

    await command.ExecuteNonQueryAsync();
  }

  public static async Task<long> DeleteStaleAsync(NpgsqlConnection connection)
  {
    await using var command = new NpgsqlCommand(
      """
      DELETE FROM card_faces f
      WHERE NOT EXISTS (
        SELECT 1
        FROM tmp_card_faces t
        WHERE t.card_id = f.card_id
          AND t.face_index = f.face_index
      );
      """,
      connection
    );

    return await command.ExecuteNonQueryAsync();
  }
}
