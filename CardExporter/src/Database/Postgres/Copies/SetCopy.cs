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

internal static class SetCopy
{
  public static async Task<long> WriteAsync(
    NpgsqlConnection connection,
    IEnumerable<SetRecord> sets
  )
  {
    long count = 0;
    await using var importer = await connection.BeginBinaryImportAsync(
      "COPY tmp_sets (code, name, release_date, age, set_type, raw) FROM STDIN (FORMAT BINARY)"
    );

    foreach (SetRecord set in sets)
    {
      await importer.StartRowAsync();
      await importer.WriteAsync(set.Code, NpgsqlDbType.Text);
      await importer.WriteAsync(set.Name, NpgsqlDbType.Text);
      await importer.WriteAsync(set.ReleaseDate, NpgsqlDbType.Date);
      await importer.WriteAsync(set.Age, NpgsqlDbType.Integer);
      await importer.WriteAsync(set.SetType, NpgsqlDbType.Text);
      await importer.WriteAsync(set.RawJson, NpgsqlDbType.Jsonb);
      count++;
    }

    await importer.CompleteAsync();
    return count;
  }
}
