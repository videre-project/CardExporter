/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Linq;


namespace CardExporter.MTGO.Parsing;

internal sealed record LookupTables(
  IReadOnlyDictionary<string, string> CardNames,
  IReadOnlyDictionary<string, string> CardNameTokens,
  IReadOnlyDictionary<string, string> SetCodes,
  IReadOnlyDictionary<string, string> Artists,
  IReadOnlyDictionary<string, string> Colors,
  IReadOnlyDictionary<string, string> ColorIdentities,
  IReadOnlyDictionary<string, string> ConvertedManaCosts,
  IReadOnlyDictionary<string, string> FlavorTexts,
  IReadOnlyDictionary<string, string> ManaCosts,
  IReadOnlyDictionary<string, string> OracleTexts,
  IReadOnlyDictionary<string, string> PromoLabels,
  IReadOnlyDictionary<string, string> Rarities,
  IReadOnlyDictionary<string, string> PowerToughnesses,
  IReadOnlyDictionary<string, string> Loyalties,
  IReadOnlyDictionary<string, string> Defenses,
  IReadOnlyDictionary<string, string> TypeNames
)
{
  public string? ResolveCardName(DigitalObjectFields fields)
  {
    return GetCardName(fields.CardNameId) ?? GetCardNameToken(fields.CardNameTokenId);
  }

  public string? ResolveSetCode(DigitalObjectFields fields)
  {
    return GetSetCode(fields.CardSetId) ?? fields.CurrentSetCode;
  }

  public string? GetCardName(string? id) => Get(CardNames, id);

  public string? GetCardNameToken(string? id) => Get(CardNameTokens, id);

  public string? GetSetCode(string? id) => Get(SetCodes, id);

  public string? GetArtist(string? id) => Get(Artists, id);

  public IReadOnlyList<string> ResolveColors(string? id) => NormalizeColors(Get(Colors, id));

  public IReadOnlyList<string> ResolveColorIdentity(string? id) => NormalizeColors(Get(ColorIdentities, id));

  public int? ResolveConvertedManaCost(string? id)
  {
    return TryParseInt(Get(ConvertedManaCosts, id), out int convertedManaCost)
      ? convertedManaCost
      : null;
  }

  public string? GetFlavorText(string? id) => Get(FlavorTexts, id);

  public string? GetManaCost(string? id) => Get(ManaCosts, id);

  public string? GetOracleText(string? id) => Get(OracleTexts, id);

  public string? GetPromoLabel(string? id) => Get(PromoLabels, id);

  public string? ResolveRarity(string? id) => NormalizeRarity(Get(Rarities, id));

  public string? GetPowerToughness(string? id) => Get(PowerToughnesses, id);

  public string? GetLoyalty(string? id) => Get(Loyalties, id);

  public string? GetDefense(string? id) => Get(Defenses, id);

  public string? GetTypeName(string? id) => Get(TypeNames, id);

  public IReadOnlyList<string> ResolveSubtypeNames(DigitalObjectFields fields)
  {
    var subtypes = new List<string>(fields.SubtypeIds.Count + 1);
    foreach (string subtypeId in fields.SubtypeIds)
    {
      if (GetTypeName(subtypeId) is string subtype)
      {
        AddDistinct(subtypes, subtype);
      }
    }

    if (fields.IsAdventure == true)
    {
      AddDistinct(subtypes, "Adventure");
    }

    return subtypes;
  }

  private static string? Get(IReadOnlyDictionary<string, string> lookup, string? id)
  {
    if (id is null || !lookup.TryGetValue(id, out string? value) || string.IsNullOrWhiteSpace(value))
    {
      return null;
    }

    return value;
  }

  private static string? NormalizeRarity(string? rarity)
  {
    if (string.IsNullOrWhiteSpace(rarity))
    {
      return null;
    }

    const string prefix = "MA_RARITY_";
    if (rarity.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
    {
      rarity = rarity[prefix.Length..];
    }

    return rarity.Replace('_', ' ').ToLowerInvariant();
  }

  private static IReadOnlyList<string> NormalizeColors(string? value)
  {
    if (string.IsNullOrWhiteSpace(value))
    {
      return [];
    }

    var colors = new List<string>(5);
    foreach (string part in value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
      string? color = part switch
      {
        "COLOR_WHITE" => "W",
        "COLOR_BLUE" => "U",
        "COLOR_BLACK" => "B",
        "COLOR_RED" => "R",
        "COLOR_GREEN" => "G",
        "COLOR_COLORLESS" => "C",
        _ when part.StartsWith("COLOR_", StringComparison.Ordinal) => part["COLOR_".Length..],
        _ => part
      };

      if (!string.IsNullOrWhiteSpace(color))
      {
        AddDistinct(colors, color);
      }
    }

    return colors;
  }

  private static void AddDistinct(List<string> values, string value)
  {
    if (!values.Contains(value, StringComparer.OrdinalIgnoreCase))
    {
      values.Add(value);
    }
  }

  private static bool TryParseInt(string? value, out int result)
  {
    result = 0;
    return !string.IsNullOrWhiteSpace(value) && int.TryParse(value, out result);
  }
}
