/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Generic;
using System.Linq;

namespace CardExporter.Database.Postgres;

internal readonly record struct StagedImportCounts(
  long SetCount,
  long CardCount,
  long ProductCount,
  long FaceCount,
  long LegalityCount
);

internal readonly record struct StaleDeleteCounts(
  long FaceCount,
  long CardCount,
  long ProductCount,
  long OracleCardCount,
  long SetCount,
  long LegalityCount
);

internal sealed record CardImageChangeSet(
  IReadOnlySet<int> AddedCatalogIds,
  IReadOnlySet<int> ModifiedCatalogIds
)
{
  public IReadOnlySet<int> AllCatalogIds =>
    AddedCatalogIds.Concat(ModifiedCatalogIds).ToHashSet();
}

internal sealed record CardDataImportResult(
  StagedImportCounts StagedCounts,
  StaleDeleteCounts DeletedCounts,
  CardImageChangeSet ImageChanges,
  ImportDatabaseState FinalDatabaseState
);
