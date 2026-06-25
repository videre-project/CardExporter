/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Linq;
using CardExporter.MTGO.Records;


namespace CardExporter.MTGO.Parsing;

internal static class SetNameResolver
{
  private static readonly string[] FullSetSuffixes =
  [
    " Draft Booster",
    " Sealed Booster",
    " Booster",
    " Foil Set",
    " Set"
  ];

  public static IReadOnlyDictionary<string, string> InferFromProducts(
    IEnumerable<ProductRecord> products
  )
  {
    return products
      .Select(static product => new
      {
        product.SetCode,
        SetName = InferFromProductName(product.Name)
      })
      .Where(static item =>
        !string.IsNullOrWhiteSpace(item.SetCode) &&
        !string.IsNullOrWhiteSpace(item.SetName))
      .GroupBy(static item => item.SetCode!, StringComparer.OrdinalIgnoreCase)
      .ToDictionary(
        static group => group.Key,
        static group => group
          .Select(static item => item.SetName!)
          .OrderBy(static name => name.Length)
          .ThenBy(static name => name, StringComparer.OrdinalIgnoreCase)
          .First(),
        StringComparer.OrdinalIgnoreCase
      );
  }

  private static string? InferFromProductName(string? productName)
  {
    if (string.IsNullOrWhiteSpace(productName))
    {
      return null;
    }

    string name = productName.Trim();
    foreach (string suffix in FullSetSuffixes)
    {
      if (!name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
      {
        continue;
      }

      string setName = name[..^suffix.Length].Trim();
      return setName.Length > 0 ? setName : null;
    }

    return null;
  }
}
