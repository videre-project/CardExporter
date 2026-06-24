/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Generic;
using System.Threading.Tasks;
using CardExporter.MTGO.Rendering.Cards;
using Npgsql;


namespace CardExporter.Database.Postgres;

internal static class CardImageChangeTracker
{
  public static async Task<CardImageChangeSet> GetChangedImageCatalogIdsAsync(NpgsqlConnection connection)
  {
    await using var command = new NpgsqlCommand(
      """
      WITH staged_cards AS (
        SELECT DISTINCT ON (id)
          c.id,
          c.oracle_id,
          c.set_code,
          NULLIF(c.name, '') AS name,
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
          NULLIF(c.promo_label, '') AS promo_label,
          c.has_activated_ability,
          c.should_work,
          c.is_foil,
          c.is_token,
          c.raw
        FROM tmp_cards c
        ORDER BY
          c.id,
          c.name IS NULL,
            c.name LIKE 'MA_CTOKEN_%',
            coalesce(length(c.name), 0) DESC
      ),
      added_cards AS (
        SELECT sc.*
        FROM staged_cards sc
        LEFT JOIN cards c ON c.id = sc.id
        WHERE c.id IS NULL
      ),
      modified_cards AS (
        SELECT sc.*
        FROM staged_cards sc
        JOIN cards c ON c.id = sc.id
        WHERE c.set_code IS DISTINCT FROM sc.set_code
          OR c.name IS DISTINCT FROM sc.name
          OR c.collector_number IS DISTINCT FROM sc.collector_number
          OR c.art_id IS DISTINCT FROM sc.art_id
          OR c.artist IS DISTINCT FROM sc.artist
          OR c.card_texture_number IS DISTINCT FROM sc.card_texture_number
          OR c.other_face_texture_number IS DISTINCT FROM sc.other_face_texture_number
          OR c.split_card_ids IS DISTINCT FROM sc.split_card_ids
          OR c.split_parent_card_id IS DISTINCT FROM sc.split_parent_card_id
          OR c.split_other_card_id IS DISTINCT FROM sc.split_other_card_id
          OR c.colors IS DISTINCT FROM sc.colors
          OR c.color_identity IS DISTINCT FROM sc.color_identity
          OR c.color_mask IS DISTINCT FROM sc.color_mask
          OR c.color_identity_mask IS DISTINCT FROM sc.color_identity_mask
          OR c.mana_value IS DISTINCT FROM sc.mana_value
          OR c.flavor_text IS DISTINCT FROM sc.flavor_text
          OR c.mana_cost IS DISTINCT FROM sc.mana_cost
          OR c.type_line IS DISTINCT FROM sc.type_line
          OR c.oracle_text IS DISTINCT FROM sc.oracle_text
          OR c.supertypes IS DISTINCT FROM sc.supertypes
          OR c.card_types IS DISTINCT FROM sc.card_types
          OR c.subtypes IS DISTINCT FROM sc.subtypes
          OR c.power IS DISTINCT FROM sc.power
          OR c.toughness IS DISTINCT FROM sc.toughness
          OR c.loyalty IS DISTINCT FROM sc.loyalty
          OR c.defense IS DISTINCT FROM sc.defense
          OR c.rarity IS DISTINCT FROM sc.rarity
          OR c.frame_style IS DISTINCT FROM sc.frame_style
          OR c.promo_label IS DISTINCT FROM sc.promo_label
          OR c.has_activated_ability IS DISTINCT FROM sc.has_activated_ability
          OR c.should_work IS DISTINCT FROM sc.should_work
          OR c.is_foil IS DISTINCT FROM sc.is_foil
          OR c.is_token IS DISTINCT FROM sc.is_token
          OR c.raw IS DISTINCT FROM sc.raw
      ),
      staged_faces AS (
        SELECT DISTINCT ON (card_id, face_index)
          f.card_id,
          f.face_index,
          f.source_catalog_id,
          NULLIF(f.name, '') AS name,
          f.colors,
          f.color_mask,
          f.mana_value,
          f.flavor_text,
          f.mana_cost,
          f.type_line,
          f.oracle_text,
          f.supertypes,
          f.card_types,
          f.subtypes,
          f.power,
          f.toughness,
          f.loyalty,
          f.defense,
          f.artist,
          f.art_id,
          f.raw
        FROM tmp_card_faces f
        ORDER BY
          f.card_id,
          f.face_index,
            f.name IS NULL,
            coalesce(length(f.name), 0) DESC
      ),
      added_faces AS (
        SELECT sf.*
        FROM staged_faces sf
        LEFT JOIN card_faces f
          ON f.card_id = sf.card_id
          AND f.face_index = sf.face_index
        WHERE f.card_id IS NULL
      ),
      modified_faces AS (
        SELECT sf.*
        FROM staged_faces sf
        JOIN card_faces f
          ON f.card_id = sf.card_id
          AND f.face_index = sf.face_index
        WHERE f.source_catalog_id IS DISTINCT FROM sf.source_catalog_id
          OR f.name IS DISTINCT FROM sf.name
          OR f.colors IS DISTINCT FROM sf.colors
          OR f.color_mask IS DISTINCT FROM sf.color_mask
          OR f.mana_value IS DISTINCT FROM sf.mana_value
          OR f.flavor_text IS DISTINCT FROM sf.flavor_text
          OR f.mana_cost IS DISTINCT FROM sf.mana_cost
          OR f.type_line IS DISTINCT FROM sf.type_line
          OR f.oracle_text IS DISTINCT FROM sf.oracle_text
          OR f.supertypes IS DISTINCT FROM sf.supertypes
          OR f.card_types IS DISTINCT FROM sf.card_types
          OR f.subtypes IS DISTINCT FROM sf.subtypes
          OR f.power IS DISTINCT FROM sf.power
          OR f.toughness IS DISTINCT FROM sf.toughness
          OR f.loyalty IS DISTINCT FROM sf.loyalty
          OR f.defense IS DISTINCT FROM sf.defense
          OR f.artist IS DISTINCT FROM sf.artist
          OR f.art_id IS DISTINCT FROM sf.art_id
          OR f.raw IS DISTINCT FROM sf.raw
      ),
      staged_products AS (
        SELECT DISTINCT ON (id)
          p.id,
          p.set_code,
          NULLIF(p.name, '') AS name,
          NULLIF(p.object_type, '') AS object_type,
          p.texture_number,
          p.is_tradable,
          p.raw
        FROM tmp_products p
        ORDER BY
          p.id,
          p.name IS NULL,
          coalesce(length(p.name), 0) DESC
      ),
      added_products AS (
        SELECT sp.*
        FROM staged_products sp
        LEFT JOIN products p ON p.id = sp.id
        WHERE p.id IS NULL
      ),
      modified_products AS (
        SELECT sp.*
        FROM staged_products sp
        JOIN products p ON p.id = sp.id
        WHERE p.set_code IS DISTINCT FROM sp.set_code
          OR p.name IS DISTINCT FROM sp.name
          OR p.object_type IS DISTINCT FROM sp.object_type
          OR p.texture_number IS DISTINCT FROM sp.texture_number
          OR p.is_tradable IS DISTINCT FROM sp.is_tradable
          OR p.raw IS DISTINCT FROM sp.raw
      ),
      face_rows_affected_by_added_cards AS (
        SELECT sf.*
        FROM staged_faces sf
        JOIN added_cards ac ON ac.id = sf.card_id
      ),
      face_rows_affected_by_modified_cards AS (
        SELECT sf.*
        FROM staged_faces sf
        JOIN modified_cards mc ON mc.id = sf.card_id
      ),
      added_affected_faces AS (
        SELECT * FROM added_faces
        UNION
        SELECT * FROM face_rows_affected_by_added_cards
      ),
      modified_affected_faces AS (
        SELECT * FROM modified_faces
        UNION
        SELECT * FROM face_rows_affected_by_modified_cards
      ),
      image_changes AS (
        SELECT
          'added'::text AS change_kind,
          CASE
            WHEN ac.raw->>'CloneId' ~ '^[0-9]+$'
              AND coalesce((ac.raw->>'IsFoil')::boolean, false)
              AND NOT (coalesce(ac.set_code, '') = ANY(@separateFoilCloneImageSetCodes))
            THEN (ac.raw->>'CloneId')::integer
            ELSE ac.id
          END AS image_catalog_id
        FROM added_cards ac

        UNION

        SELECT
          'modified'::text AS change_kind,
          CASE
            WHEN mc.raw->>'CloneId' ~ '^[0-9]+$'
              AND coalesce((mc.raw->>'IsFoil')::boolean, false)
              AND NOT (coalesce(mc.set_code, '') = ANY(@separateFoilCloneImageSetCodes))
            THEN (mc.raw->>'CloneId')::integer
            ELSE mc.id
          END AS image_catalog_id
        FROM modified_cards mc

        UNION

        SELECT
          'added'::text AS change_kind,
          CASE
            WHEN af.raw->>'CloneId' ~ '^[0-9]+$'
              AND coalesce((af.raw->>'IsFoil')::boolean, false)
              AND NOT (coalesce(sc.set_code, '') = ANY(@separateFoilCloneImageSetCodes))
            THEN (af.raw->>'CloneId')::integer
            ELSE af.source_catalog_id
          END AS image_catalog_id
        FROM added_affected_faces af
        JOIN staged_cards sc ON sc.id = af.card_id
        WHERE af.source_catalog_id IS NOT NULL

        UNION

        SELECT
          'modified'::text AS change_kind,
          CASE
            WHEN mf.raw->>'CloneId' ~ '^[0-9]+$'
              AND coalesce((mf.raw->>'IsFoil')::boolean, false)
              AND NOT (coalesce(sc.set_code, '') = ANY(@separateFoilCloneImageSetCodes))
            THEN (mf.raw->>'CloneId')::integer
            ELSE mf.source_catalog_id
          END AS image_catalog_id
        FROM modified_affected_faces mf
        JOIN staged_cards sc ON sc.id = mf.card_id
        WHERE mf.source_catalog_id IS NOT NULL

        UNION

        SELECT
          'added'::text AS change_kind,
          ap.id AS image_catalog_id
        FROM added_products ap

        UNION

        SELECT
          'modified'::text AS change_kind,
          mp.id AS image_catalog_id
        FROM modified_products mp
      )
      SELECT DISTINCT change_kind, image_catalog_id
      FROM image_changes
      WHERE image_catalog_id IS NOT NULL
      ORDER BY image_catalog_id, change_kind;
      """,
      connection
    );
    command.Parameters.AddWithValue(
      "separateFoilCloneImageSetCodes",
      FoilCloneImagePolicy.SeparateFoilCloneImageSetCodes
    );

    HashSet<int> addedCatalogIds = new HashSet<int>();
    HashSet<int> modifiedCatalogIds = new HashSet<int>();
    await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
      string changeKind = reader.GetString(0);
      int catalogId = reader.GetInt32(1);
      if (changeKind == "added")
      {
        addedCatalogIds.Add(catalogId);
      }
      else
      {
        modifiedCatalogIds.Add(catalogId);
      }
    }

    addedCatalogIds.ExceptWith(modifiedCatalogIds);
    return new CardImageChangeSet(addedCatalogIds, modifiedCatalogIds);
  }
}
