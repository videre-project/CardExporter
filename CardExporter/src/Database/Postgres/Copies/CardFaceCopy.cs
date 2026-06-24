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

internal static class CardFaceCopy
{
  public static async Task<long> WriteAsync(
    NpgsqlConnection connection,
    IEnumerable<CardFace> cardFaces
  )
  {
    long count = 0;
    await using var importer = await connection.BeginBinaryImportAsync(
      """
      COPY tmp_card_faces (
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
      ) FROM STDIN (FORMAT BINARY)
      """
    );

    foreach (CardFace cardFace in cardFaces)
    {
      await importer.StartRowAsync();
      await importer.WriteAsync(cardFace.CardId, NpgsqlDbType.Integer);
      await importer.WriteAsync(cardFace.FaceIndex, NpgsqlDbType.Smallint);
      await importer.WriteAsync(cardFace.SourceCatalogId, NpgsqlDbType.Integer);
      await importer.WriteAsync(cardFace.Name, NpgsqlDbType.Text);
      await importer.WriteAsync(JsonSerializer.Serialize(cardFace.Colors), NpgsqlDbType.Jsonb);
      await importer.WriteAsync(cardFace.ColorMask, NpgsqlDbType.Integer);
      await importer.WriteAsync(cardFace.ManaValue, NpgsqlDbType.Numeric);
      await importer.WriteAsync(cardFace.FlavorText, NpgsqlDbType.Text);
      await importer.WriteAsync(cardFace.ManaCost, NpgsqlDbType.Text);
      await importer.WriteAsync(cardFace.TypeLine, NpgsqlDbType.Text);
      await importer.WriteAsync(cardFace.OracleText, NpgsqlDbType.Text);
      await importer.WriteAsync(JsonSerializer.Serialize(cardFace.Supertypes), NpgsqlDbType.Jsonb);
      await importer.WriteAsync(JsonSerializer.Serialize(cardFace.CardTypes), NpgsqlDbType.Jsonb);
      await importer.WriteAsync(JsonSerializer.Serialize(cardFace.Subtypes), NpgsqlDbType.Jsonb);
      await importer.WriteAsync(cardFace.Power, NpgsqlDbType.Text);
      await importer.WriteAsync(cardFace.Toughness, NpgsqlDbType.Text);
      await importer.WriteAsync(cardFace.Loyalty, NpgsqlDbType.Text);
      await importer.WriteAsync(cardFace.Defense, NpgsqlDbType.Text);
      await importer.WriteAsync(cardFace.Artist, NpgsqlDbType.Text);
      await importer.WriteAsync(cardFace.ArtId, NpgsqlDbType.Integer);
      await importer.WriteAsync(cardFace.RawJson, NpgsqlDbType.Jsonb);
      count++;
    }

    await importer.CompleteAsync();
    return count;
  }
}
