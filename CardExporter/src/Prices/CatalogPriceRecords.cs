/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;


namespace CardExporter.Prices;

internal sealed record CatalogPriceDefinitionRecord(
  string Source,
  int CatalogId,
  string? SourceName,
  string? SourceCardset,
  string? SourceRarity,
  string? SourceVersion,
  bool? SourceFoil,
  string RawJson
);

internal sealed record CatalogPriceRecord(
  string Source,
  DateOnly PriceDate,
  int CatalogId,
  decimal SellPrice
);

internal sealed record PriceDefinitionImportResult(
  long StagedCount,
  long ImportedCount,
  long UnknownCatalogIdCount
);

internal sealed record PriceHistoryImportResult(
  long StagedCount,
  long ImportedCount,
  long UnknownCatalogIdCount
);
