/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Generic;
using System.Threading.Tasks;
using CardExporter.MTGO.Records;
using Npgsql;
using NpgsqlTypes;


namespace CardExporter.Database.Postgres;

internal static class CardCatalogVariantCopy
{
  public static async Task<long> WriteAsync(
    NpgsqlConnection connection,
    IEnumerable<CardCatalogVariantRecord> variants
  )
  {
    long count = 0;
    await using var importer = await connection.BeginBinaryImportAsync(
      """
      COPY tmp_card_catalog_variants (
        catalog_id,
        card_id,
        variant_type,
        set_code,
        name,
        card_texture_number,
        is_foil,
        is_token,
        raw
      ) FROM STDIN (FORMAT BINARY)
      """
    );

    foreach (CardCatalogVariantRecord variant in variants)
    {
      await importer.StartRowAsync();
      await importer.WriteAsync(variant.CatalogId, NpgsqlDbType.Integer);
      await importer.WriteAsync(variant.CardId, NpgsqlDbType.Integer);
      await importer.WriteAsync(variant.VariantType, NpgsqlDbType.Text);
      await importer.WriteAsync(variant.SetCode, NpgsqlDbType.Text);
      await importer.WriteAsync(variant.Name, NpgsqlDbType.Text);
      await importer.WriteAsync(variant.CardTextureNumber, NpgsqlDbType.Integer);
      await importer.WriteAsync(variant.IsFoil, NpgsqlDbType.Boolean);
      await importer.WriteAsync(variant.IsToken, NpgsqlDbType.Boolean);
      await importer.WriteAsync(variant.RawJson, NpgsqlDbType.Jsonb);
      count++;
    }

    await importer.CompleteAsync();
    return count;
  }
}
