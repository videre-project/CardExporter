/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Threading.Tasks;
using CardExporter.MTGO;
using Npgsql;


namespace CardExporter.Database.Postgres;

internal static class StagingCopyWriter
{
  public static async Task<StagedImportCounts> CopyAsync(
    NpgsqlConnection connection,
    Parser parser
  )
  {
    long setCount = await SetCopy.WriteAsync(connection, parser.EnumerateSets());
    long cardCount = await CardCopy.WriteAsync(connection, parser.EnumerateCards());
    long productCount = await ProductCopy.WriteAsync(connection, parser.EnumerateProducts());
    long cardCatalogVariantCount = await CardCatalogVariantCopy.WriteAsync(
      connection,
      parser.EnumerateCardCatalogVariants()
    );
    long faceCount = await CardFaceCopy.WriteAsync(connection, parser.EnumerateCardFaces());
    long legalityCount = await CardLegalityCopy.WriteAsync(connection, parser.EnumerateCardLegalities());

    return new StagedImportCounts(
      setCount,
      cardCount,
      productCount,
      cardCatalogVariantCount,
      faceCount,
      legalityCount
    );
  }
}
