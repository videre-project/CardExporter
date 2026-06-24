/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using CardExporter.MTGO.Rendering;

namespace CardExporter.MTGO.Rendering.Mana;

internal sealed class ManaSymbolBamlExtractor
{
  private const string ManaSymbolsResourceName = "manasymbols.baml";

  public string ExtractManaSymbolsXaml(string cardAssemblyPath)
  {
    return new BamlResourceExtractor().ExtractXaml(cardAssemblyPath, ManaSymbolsResourceName);
  }
}
