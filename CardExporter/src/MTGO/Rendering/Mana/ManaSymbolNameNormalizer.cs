/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using MTGOSDK.API;


namespace CardExporter.MTGO.Rendering.Mana;

internal static partial class ManaSymbolNameNormalizer
{
  private static readonly IReadOnlyDictionary<string, string> ColorLetters =
    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
      ["white"] = "W",
      ["blue"] = "U",
      ["black"] = "B",
      ["red"] = "R",
      ["green"] = "G",
      ["colorless"] = "C"
    };

  public static bool TryNormalize(string mtgoKey, out string normalizedSymbol, out string slug)
  {
    normalizedSymbol = string.Empty;
    slug = string.Empty;

    if (IsExcludedNonManaKey(mtgoKey))
    {
      return false;
    }

    if (string.Equals(mtgoKey, "#p--", StringComparison.OrdinalIgnoreCase))
    {
      normalizedSymbol = "{P}";
      slug = ToSlug(normalizedSymbol);
      return true;
    }

    foreach (string candidate in EnumerateNormalizerCandidates(mtgoKey))
    {
      string normalized = MtgoTextNormalizer.NormalizeManaCost(candidate);
      if (IsBraceSymbol(normalized))
      {
        normalizedSymbol = normalized;
        slug = ToSlug(normalized);
        return true;
      }
    }

    if (TryNormalizeNamedKey(mtgoKey, out normalizedSymbol))
    {
      slug = ToSlug(normalizedSymbol);
      return true;
    }

    return false;
  }

  internal static string ToSlug(string normalizedSymbol)
  {
    string value = normalizedSymbol
      .Replace("{", string.Empty, StringComparison.Ordinal)
      .Replace("}", string.Empty, StringComparison.Ordinal)
      .Replace("/", string.Empty, StringComparison.Ordinal)
      .Replace("+", "plus", StringComparison.Ordinal)
      .Replace("-", "minus", StringComparison.Ordinal);

    return value.ToUpperInvariant();
  }

  private static bool IsExcludedNonManaKey(string mtgoKey) =>
    string.Equals(mtgoKey, "D", StringComparison.OrdinalIgnoreCase) ||
    string.Equals(mtgoKey, "L", StringComparison.OrdinalIgnoreCase) ||
    string.Equals(mtgoKey, "H", StringComparison.OrdinalIgnoreCase);

  private static IEnumerable<string> EnumerateNormalizerCandidates(string mtgoKey)
  {
    yield return mtgoKey;

    string trimmed = mtgoKey.Trim();
    if (!string.Equals(trimmed, mtgoKey, StringComparison.Ordinal))
    {
      yield return trimmed;
    }

    if (trimmed.StartsWith('#'))
    {
      string withoutTrailingHyphen = trimmed.TrimEnd('-');
      if (!string.Equals(withoutTrailingHyphen, trimmed, StringComparison.Ordinal))
      {
        yield return withoutTrailingHyphen;
      }

      if (!trimmed.EndsWith('-'))
      {
        yield return trimmed + "-";
      }
    }
  }

  private static bool TryNormalizeNamedKey(string mtgoKey, out string normalizedSymbol)
  {
    normalizedSymbol = string.Empty;

    if (string.Equals(mtgoKey, "Mana__Snow_icon", StringComparison.OrdinalIgnoreCase))
    {
      normalizedSymbol = "{S}";
      return true;
    }

    if (string.Equals(mtgoKey, "Mana__Tap_icon", StringComparison.OrdinalIgnoreCase))
    {
      normalizedSymbol = "{T}";
      return true;
    }

    if (string.Equals(mtgoKey, "Mana__UnTap_icon", StringComparison.OrdinalIgnoreCase))
    {
      normalizedSymbol = "{Q}";
      return true;
    }

    Match phyrexianMatch = PhyrexianIconKey().Match(mtgoKey);
    if (phyrexianMatch.Success && ColorLetters.TryGetValue(phyrexianMatch.Groups["color"].Value, out string? phyrexianColor))
    {
      normalizedSymbol = $"{{{phyrexianColor}/P}}";
      return true;
    }

    Match manaDrawingMatch = ManaDrawingKey().Match(mtgoKey);
    if (manaDrawingMatch.Success && ColorLetters.TryGetValue(manaDrawingMatch.Groups["color"].Value, out string? manaColor))
    {
      normalizedSymbol = $"{{{manaColor}}}";
      return true;
    }

    return false;
  }

  private static bool IsBraceSymbol(string value) =>
    value.StartsWith('{') &&
    value.EndsWith('}') &&
    value.Contains('}') &&
    !value.Contains('#', StringComparison.Ordinal);

  [GeneratedRegex("^Mana__Phyrexian_(?<color>White|Blue|Black|Red|Green)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
  private static partial Regex PhyrexianIconKey();

  [GeneratedRegex("^(?<color>White|Blue|Black|Red|Green|Colorless)ManaDrawing$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
  private static partial Regex ManaDrawingKey();
}
