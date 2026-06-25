/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Threading.Tasks;
using Npgsql;


namespace CardExporter.Database.Postgres;

internal static class CardCatalogVariantMerge
{
  public static async Task MergeAsync(NpgsqlConnection connection)
  {
    await using var command = new NpgsqlCommand(
      """
      INSERT INTO card_catalog_variants (
        catalog_id,
        card_id,
        variant_type,
        set_code,
        name,
        card_texture_number,
        is_foil,
        is_token,
        raw,
        last_seen_at
      )
      SELECT DISTINCT ON (catalog_id)
        v.catalog_id,
        v.card_id,
        v.variant_type,
        v.set_code,
        NULLIF(v.name, ''),
        v.card_texture_number,
        v.is_foil,
        v.is_token,
        v.raw,
        now()
      FROM tmp_card_catalog_variants v
      ORDER BY
        v.catalog_id,
        v.name IS NULL,
        coalesce(length(v.name), 0) DESC
      ON CONFLICT (catalog_id) DO UPDATE SET
        card_id = EXCLUDED.card_id,
        variant_type = EXCLUDED.variant_type,
        set_code = EXCLUDED.set_code,
        name = EXCLUDED.name,
        card_texture_number = EXCLUDED.card_texture_number,
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
      DELETE FROM card_catalog_variants v
      WHERE NOT EXISTS (
        SELECT 1
        FROM tmp_card_catalog_variants t
        WHERE t.catalog_id = v.catalog_id
      );
      """,
      connection
    );

    return await command.ExecuteNonQueryAsync();
  }
}
