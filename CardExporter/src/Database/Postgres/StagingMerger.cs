/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Threading.Tasks;
using Npgsql;


namespace CardExporter.Database.Postgres;

internal static class StagingMerger
{
  public static async Task MergeAsync(NpgsqlConnection connection)
  {
    await SetMerge.MergeAsync(connection);
    await SetMerge.MergeReferencedCodesAsync(connection);
    await OracleCardMerge.MergeAsync(connection);
    await CardMerge.MergeAsync(connection);
    await ProductMerge.MergeAsync(connection);
    await CardFaceMerge.MergeAsync(connection);
    await CardLegalityMerge.MergeAsync(connection);
  }

  public static async Task<StaleDeleteCounts> DeleteStaleAsync(NpgsqlConnection connection)
  {
    long legalityCount = await CardLegalityMerge.DeleteStaleAsync(connection);
    long faceCount = await CardFaceMerge.DeleteStaleAsync(connection);
    long cardCount = await CardMerge.DeleteStaleAsync(connection);
    long productCount = await ProductMerge.DeleteStaleAsync(connection);
    long oracleCardCount = await OracleCardMerge.DeleteStaleAsync(connection);
    long setCount = await SetMerge.DeleteStaleAsync(connection);

    return new StaleDeleteCounts(
      faceCount,
      cardCount,
      productCount,
      oracleCardCount,
      setCount,
      legalityCount
    );
  }
}
