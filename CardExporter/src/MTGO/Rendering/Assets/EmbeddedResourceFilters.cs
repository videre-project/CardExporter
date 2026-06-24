/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;


namespace CardExporter.MTGO.Rendering.Assets;

internal static class EmbeddedResourceFilters
{
  public static bool IsImageResource(string key) =>
    key.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
    key.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
    key.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase);
}
