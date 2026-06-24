/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using CardExporter.MTGO.Records;


namespace CardExporter.MTGO.Parsing;

internal sealed record ParserDebugInfo(
  string SourceFile,
  DigitalObjectFields Fields,
  string? CardName,
  string? CardNameToken,
  string? SetCode,
  CardRecord? CardRecord
);
