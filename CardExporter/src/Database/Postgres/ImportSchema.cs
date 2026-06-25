/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Npgsql;


namespace CardExporter.Database.Postgres;

internal static class ImportSchema
{
  private const string SchemaResourceName = "CardExporter.Database.Postgres.Schema.sql";
  private static string? s_schemaSql;

  public static async Task EnsureCurrentSchemaAsync(NpgsqlConnection connection)
  {
    await using var command = new NpgsqlCommand(ReadCurrentSchemaSql(), connection);
    await command.ExecuteNonQueryAsync();
  }

  private static string ReadCurrentSchemaSql()
  {
    if (s_schemaSql is not null)
    {
      return s_schemaSql;
    }

    Assembly assembly = typeof(ImportSchema).Assembly;
    using Stream stream = assembly.GetManifestResourceStream(SchemaResourceName) ??
      throw new InvalidOperationException($"Embedded schema resource {SchemaResourceName} was not found.");
    using var reader = new StreamReader(stream);
    s_schemaSql = reader.ReadToEnd();
    return s_schemaSql;
  }

  public static async Task CreateStagingTablesAsync(NpgsqlConnection connection)
  {
    await using var command = new NpgsqlCommand(
      """
      CREATE TEMP TABLE tmp_sets (
        code TEXT NOT NULL,
        name TEXT NULL,
        release_date DATE NULL,
        age INTEGER NULL,
        set_type TEXT NULL,
        raw JSONB NOT NULL
      ) ON COMMIT DROP;

      CREATE TEMP TABLE tmp_cards (
        id INTEGER NOT NULL,
        oracle_id UUID NOT NULL,
        set_code TEXT NULL,
        name TEXT NULL,
        collector_number TEXT NULL,
        art_id INTEGER NULL,
        artist TEXT NULL,
        card_texture_number INTEGER NULL,
        other_face_texture_number INTEGER NULL,
        split_card_ids JSONB NOT NULL,
        split_parent_card_id INTEGER NULL,
        split_other_card_id INTEGER NULL,
        colors JSONB NOT NULL,
        color_identity JSONB NOT NULL,
        color_mask INTEGER NOT NULL,
        color_identity_mask INTEGER NOT NULL,
        mana_value NUMERIC NULL,
        flavor_text TEXT NULL,
        mana_cost TEXT NULL,
        type_line TEXT NULL,
        oracle_text TEXT NULL,
        supertypes JSONB NOT NULL,
        card_types JSONB NOT NULL,
        subtypes JSONB NOT NULL,
        power TEXT NULL,
        toughness TEXT NULL,
        loyalty TEXT NULL,
        defense TEXT NULL,
        rarity TEXT NULL,
        frame_style INTEGER NULL,
        promo_label TEXT NULL,
        has_activated_ability BOOLEAN NULL,
        should_work BOOLEAN NULL,
        is_foil BOOLEAN NULL,
        is_token BOOLEAN NULL,
        raw JSONB NOT NULL
      ) ON COMMIT DROP;

      CREATE TEMP TABLE tmp_products (
        id INTEGER NOT NULL,
        set_code TEXT NULL,
        name TEXT NULL,
        object_type TEXT NULL,
        texture_number INTEGER NULL,
        is_tradable BOOLEAN NULL,
        raw JSONB NOT NULL
      ) ON COMMIT DROP;

      CREATE TEMP TABLE tmp_card_catalog_variants (
        catalog_id INTEGER NOT NULL,
        card_id INTEGER NOT NULL,
        variant_type TEXT NOT NULL,
        set_code TEXT NULL,
        name TEXT NULL,
        card_texture_number INTEGER NULL,
        is_foil BOOLEAN NULL,
        is_token BOOLEAN NULL,
        raw JSONB NOT NULL
      ) ON COMMIT DROP;

      CREATE TEMP TABLE tmp_card_faces (
        card_id INTEGER NOT NULL,
        face_index SMALLINT NOT NULL,
        source_catalog_id INTEGER NULL,
        name TEXT NULL,
        colors JSONB NOT NULL,
        color_mask INTEGER NOT NULL,
        mana_value NUMERIC NULL,
        flavor_text TEXT NULL,
        mana_cost TEXT NULL,
        type_line TEXT NULL,
        oracle_text TEXT NULL,
        supertypes JSONB NOT NULL,
        card_types JSONB NOT NULL,
        subtypes JSONB NOT NULL,
        power TEXT NULL,
        toughness TEXT NULL,
        loyalty TEXT NULL,
        defense TEXT NULL,
        artist TEXT NULL,
        art_id INTEGER NULL,
        raw JSONB NOT NULL
      ) ON COMMIT DROP;

      CREATE TEMP TABLE tmp_card_legalities (
        oracle_id UUID NOT NULL,
        format_code TEXT NOT NULL,
        status TEXT NOT NULL,
        source_rule_set_id TEXT NOT NULL
      ) ON COMMIT DROP;
      """,
      connection
    );

    await command.ExecuteNonQueryAsync();
  }

  public static async Task CreateLegalityStagingTableAsync(NpgsqlConnection connection)
  {
    await using var command = new NpgsqlCommand(
      """
      CREATE TEMP TABLE tmp_card_legalities (
        oracle_id UUID NOT NULL,
        format_code TEXT NOT NULL,
        status TEXT NOT NULL,
        source_rule_set_id TEXT NOT NULL
      ) ON COMMIT DROP;
      """,
      connection
    );

    await command.ExecuteNonQueryAsync();
  }
}
