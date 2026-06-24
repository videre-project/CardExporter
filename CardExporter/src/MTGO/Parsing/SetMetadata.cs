/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;


namespace CardExporter.MTGO.Parsing;

internal sealed record SetMetadata(
  string? Name,
  DateOnly? ReleaseDate,
  int? Age,
  string? SetType
);
