/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Threading.Tasks;
using Npgsql;


namespace CardExporter.Database.Postgres;

internal static class CardMerge
{
  public static async Task MergeAsync(NpgsqlConnection connection)
  {
    await using var command = new NpgsqlCommand(
      """
      INSERT INTO cards (
        id,
        oracle_id,
        set_code,
        name,
        collector_number,
        art_id,
        artist,
        card_texture_number,
        other_face_texture_number,
        split_card_ids,
        split_parent_card_id,
        split_other_card_id,
        colors,
        color_identity,
        color_mask,
        color_identity_mask,
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
        rarity,
        frame_style,
        promo_label,
        has_activated_ability,
        should_work,
        is_foil,
        is_token,
        raw,
        last_seen_at
      )
      SELECT DISTINCT ON (id)
        c.id,
        c.oracle_id,
        c.set_code,
        NULLIF(c.name, ''),
        c.collector_number,
        c.art_id,
        c.artist,
        c.card_texture_number,
        c.other_face_texture_number,
        c.split_card_ids,
        c.split_parent_card_id,
        c.split_other_card_id,
        c.colors,
        c.color_identity,
        c.color_mask,
        c.color_identity_mask,
        c.mana_value,
        c.flavor_text,
        c.mana_cost,
        c.type_line,
        c.oracle_text,
        c.supertypes,
        c.card_types,
        c.subtypes,
        c.power,
        c.toughness,
        c.loyalty,
        c.defense,
        c.rarity,
        c.frame_style,
        NULLIF(c.promo_label, ''),
        c.has_activated_ability,
        c.should_work,
        c.is_foil,
        c.is_token,
        c.raw,
        now()
      FROM tmp_cards c
      ORDER BY
        c.id,
        c.name IS NULL,
        c.name LIKE 'MA_CTOKEN_%',
        coalesce(length(c.name), 0) DESC
      ON CONFLICT (id) DO UPDATE SET
        oracle_id = EXCLUDED.oracle_id,
        set_code = EXCLUDED.set_code,
        name = EXCLUDED.name,
        collector_number = EXCLUDED.collector_number,
        art_id = EXCLUDED.art_id,
        artist = EXCLUDED.artist,
        card_texture_number = EXCLUDED.card_texture_number,
        other_face_texture_number = EXCLUDED.other_face_texture_number,
        split_card_ids = EXCLUDED.split_card_ids,
        split_parent_card_id = EXCLUDED.split_parent_card_id,
        split_other_card_id = EXCLUDED.split_other_card_id,
        colors = EXCLUDED.colors,
        color_identity = EXCLUDED.color_identity,
        color_mask = EXCLUDED.color_mask,
        color_identity_mask = EXCLUDED.color_identity_mask,
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
        rarity = EXCLUDED.rarity,
        frame_style = EXCLUDED.frame_style,
        promo_label = EXCLUDED.promo_label,
        has_activated_ability = EXCLUDED.has_activated_ability,
        should_work = EXCLUDED.should_work,
        is_foil = EXCLUDED.is_foil,
        is_token = EXCLUDED.is_token,
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
      DELETE FROM cards c
      WHERE NOT EXISTS (
        SELECT 1
        FROM tmp_cards t
        WHERE t.id = c.id
      );
      """,
      connection
    );

    return await command.ExecuteNonQueryAsync();
  }
}
