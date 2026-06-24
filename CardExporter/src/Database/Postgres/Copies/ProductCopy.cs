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

internal static class ProductCopy
{
  public static async Task<long> WriteAsync(
    NpgsqlConnection connection,
    IEnumerable<ProductRecord> products
  )
  {
    long count = 0;
    await using var importer = await connection.BeginBinaryImportAsync(
      """
      COPY tmp_products (
        id,
        set_code,
        name,
        object_type,
        texture_number,
        is_tradable,
        raw
      ) FROM STDIN (FORMAT BINARY)
      """
    );

    foreach (ProductRecord product in products)
    {
      await importer.StartRowAsync();
      await importer.WriteAsync(product.Id, NpgsqlDbType.Integer);
      await importer.WriteAsync(product.SetCode, NpgsqlDbType.Text);
      await importer.WriteAsync(product.Name, NpgsqlDbType.Text);
      await importer.WriteAsync(product.ObjectType, NpgsqlDbType.Text);
      await importer.WriteAsync(product.TextureNumber, NpgsqlDbType.Integer);
      await importer.WriteAsync(product.IsTradable, NpgsqlDbType.Boolean);
      await importer.WriteAsync(product.RawJson, NpgsqlDbType.Jsonb);
      count++;
    }

    await importer.CompleteAsync();
    return count;
  }
}
