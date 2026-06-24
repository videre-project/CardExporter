/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using CardExporter.MTGO.Records;
using Npgsql;
using NpgsqlTypes;


namespace CardExporter.Database.Postgres;

internal static class CardCopy
{
  public static async Task<long> WriteAsync(
    NpgsqlConnection connection,
    IEnumerable<CardRecord> cards
  )
  {
    long count = 0;
    await using var importer = await connection.BeginBinaryImportAsync(
      """
      COPY tmp_cards (
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
        raw
      ) FROM STDIN (FORMAT BINARY)
      """
    );

    foreach (CardRecord card in cards)
    {
      await importer.StartRowAsync();
      await importer.WriteAsync(card.Id, NpgsqlDbType.Integer);
      await importer.WriteAsync(card.OracleId, NpgsqlDbType.Uuid);
      await importer.WriteAsync(card.SetCode, NpgsqlDbType.Text);
      await importer.WriteAsync(card.Name, NpgsqlDbType.Text);
      await importer.WriteAsync(card.CollectorNumber, NpgsqlDbType.Text);
      await importer.WriteAsync(card.ArtId, NpgsqlDbType.Integer);
      await importer.WriteAsync(card.Artist, NpgsqlDbType.Text);
      await importer.WriteAsync(card.CardTextureNumber, NpgsqlDbType.Integer);
      await importer.WriteAsync(card.OtherFaceTextureNumber, NpgsqlDbType.Integer);
      await importer.WriteAsync(JsonSerializer.Serialize(card.SplitCardIds), NpgsqlDbType.Jsonb);
      await importer.WriteAsync(card.SplitParentCardId, NpgsqlDbType.Integer);
      await importer.WriteAsync(card.SplitOtherCardId, NpgsqlDbType.Integer);
      await importer.WriteAsync(JsonSerializer.Serialize(card.Colors), NpgsqlDbType.Jsonb);
      await importer.WriteAsync(JsonSerializer.Serialize(card.ColorIdentity), NpgsqlDbType.Jsonb);
      await importer.WriteAsync(card.ColorMask, NpgsqlDbType.Integer);
      await importer.WriteAsync(card.ColorIdentityMask, NpgsqlDbType.Integer);
      await importer.WriteAsync(card.ManaValue, NpgsqlDbType.Numeric);
      await importer.WriteAsync(card.FlavorText, NpgsqlDbType.Text);
      await importer.WriteAsync(card.ManaCost, NpgsqlDbType.Text);
      await importer.WriteAsync(card.TypeLine, NpgsqlDbType.Text);
      await importer.WriteAsync(card.OracleText, NpgsqlDbType.Text);
      await importer.WriteAsync(JsonSerializer.Serialize(card.Supertypes), NpgsqlDbType.Jsonb);
      await importer.WriteAsync(JsonSerializer.Serialize(card.CardTypes), NpgsqlDbType.Jsonb);
      await importer.WriteAsync(JsonSerializer.Serialize(card.Subtypes), NpgsqlDbType.Jsonb);
      await importer.WriteAsync(card.Power, NpgsqlDbType.Text);
      await importer.WriteAsync(card.Toughness, NpgsqlDbType.Text);
      await importer.WriteAsync(card.Loyalty, NpgsqlDbType.Text);
      await importer.WriteAsync(card.Defense, NpgsqlDbType.Text);
      await importer.WriteAsync(card.Rarity, NpgsqlDbType.Text);
      await importer.WriteAsync(card.FrameStyle, NpgsqlDbType.Integer);
      await importer.WriteAsync(card.PromoLabel, NpgsqlDbType.Text);
      await importer.WriteAsync(card.HasActivatedAbility, NpgsqlDbType.Boolean);
      await importer.WriteAsync(card.ShouldWork, NpgsqlDbType.Boolean);
      await importer.WriteAsync(card.IsFoil, NpgsqlDbType.Boolean);
      await importer.WriteAsync(card.IsToken, NpgsqlDbType.Boolean);
      await importer.WriteAsync(card.RawJson, NpgsqlDbType.Jsonb);
      count++;
    }

    await importer.CompleteAsync();
    return count;
  }
}
