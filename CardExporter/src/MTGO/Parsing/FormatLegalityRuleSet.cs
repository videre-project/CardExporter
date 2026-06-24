/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Generic;


namespace CardExporter.MTGO.Parsing;

internal sealed record FormatLegalityRuleSet(
  string SourceRuleSetId,
  string FormatCode,
  IReadOnlyList<SetLegalityRule> SetRules,
  IReadOnlyDictionary<string, string> CardNameStatuses,
  bool RequiresCommonPrinting
);

internal sealed record SetLegalityRule(
  IReadOnlyList<SetRangeToken> RangeTokens,
  IReadOnlySet<string> SetTypes
);

internal sealed record SetRangeToken(
  bool IsExcluded,
  bool IsRange,
  string? StartSetCode,
  string? EndSetCode
);
