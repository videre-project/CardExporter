/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;


namespace CardExporter.MTGO.Records;

internal sealed record CardLegality(
  Guid OracleId,
  string FormatCode,
  string Status,
  string SourceRuleSetId
);
