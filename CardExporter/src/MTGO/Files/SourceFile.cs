/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;


namespace CardExporter.MTGO.Files;

internal sealed record SourceFile(
  string RelativePath,
  long ByteCount,
  DateTime ModifiedAtUtc,
  string Sha256
);
