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

internal static class CardLegalityCopy
{
  public static async Task<long> WriteAsync(
    NpgsqlConnection connection,
    IEnumerable<CardLegality> legalities
  )
  {
    long count = 0;
    await using var importer = await connection.BeginBinaryImportAsync(
      """
      COPY tmp_card_legalities (
        oracle_id,
        format_code,
        status,
        source_rule_set_id
      ) FROM STDIN (FORMAT BINARY)
      """
    );

    foreach (CardLegality legality in legalities)
    {
      await importer.StartRowAsync();
      await importer.WriteAsync(legality.OracleId, NpgsqlDbType.Uuid);
      await importer.WriteAsync(legality.FormatCode, NpgsqlDbType.Text);
      await importer.WriteAsync(legality.Status, NpgsqlDbType.Text);
      await importer.WriteAsync(legality.SourceRuleSetId, NpgsqlDbType.Text);
      count++;
    }

    await importer.CompleteAsync();
    return count;
  }
}
