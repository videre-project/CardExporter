/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Linq;
using MTGOSDK.API.Collection;


namespace CardExporter.MTGO.Parsing;

internal static class CardAdapter
{
  public static Card CreateCard(
    int catalogId,
    string? name,
    IReadOnlyList<string> colors,
    int? convertedManaCost,
    string? manaCost,
    string? oracleText,
    IEnumerable<string> types,
    IReadOnlyList<string> subtypes,
    string? artist,
    int? artId,
    string? collectorInfo,
    int? collectorNumber,
    string? flavorText,
    string? power,
    string? toughness,
    string? loyalty,
    string? defense,
    bool? isToken,
    string? rarity
  )
  {
    var definition = new
    {
      Id = catalogId,
      Name = name ?? string.Empty,
      Description = name ?? string.Empty,
      IsOpenable = false,
      IsSealedProduct = false,
      IsDigitalObject = true,
      IsBooster = false,
      IsCard = true,
      IsTicket = false,
      IsTradable = true,
      HasPremiumOdds = false,
      ColorDisplayString = string.Concat(colors),
      ManaCost = manaCost ?? string.Empty,
      ConvertedManaCost = convertedManaCost ?? 0,
      RulesText = oracleText ?? string.Empty,
      Types = string.Join(", ", types.Where(value => !string.IsNullOrWhiteSpace(value))),
      Subtypes = subtypes.ToList(),
      ArtistName = artist ?? string.Empty,
      ArtId = artId ?? 0,
      CollectorInfo = collectorInfo ?? string.Empty,
      CollectorNumber = collectorNumber ?? 0,
      FlavorText = flavorText ?? string.Empty,
      Power = power ?? string.Empty,
      Toughness = toughness ?? string.Empty,
      InitialLoyalty = loyalty ?? string.Empty,
      InitialBattleDefense = defense ?? string.Empty,
      IsToken = isToken ?? false,
      Rarity = new
      {
        Name = rarity ?? string.Empty
      }
    };

    return new Card(definition);
  }

  public static string? BuildTypeLine(IEnumerable<string> types, IEnumerable<string> subtypes)
  {
    List<string> mainTypes = ToDistinctList(types);
    List<string> subtypeList = ToDistinctList(subtypes);

    if (mainTypes.Count == 0)
    {
      return subtypeList.Count == 0 ? null : string.Join(" ", subtypeList);
    }

    string typeLine = string.Join(" ", mainTypes);
    if (subtypeList.Count > 0)
    {
      typeLine += " - " + string.Join(" ", subtypeList);
    }

    return typeLine;
  }

  public static IReadOnlyList<string> SplitColorDisplayString(string? colors)
  {
    if (string.IsNullOrWhiteSpace(colors))
    {
      return [];
    }

    var values = new List<string>(colors.Length);
    foreach (char symbol in colors)
    {
      if (!char.IsWhiteSpace(symbol))
      {
        AddDistinct(values, symbol.ToString());
      }
    }

    return values;
  }

  public static string? NullIfEmpty(string? value)
  {
    return string.IsNullOrWhiteSpace(value) ? null : value;
  }

  private static List<string> ToDistinctList(IEnumerable<string> values)
  {
    var distinct = new List<string>();
    foreach (string value in values)
    {
      if (!string.IsNullOrWhiteSpace(value))
      {
        AddDistinct(distinct, value);
      }
    }

    return distinct;
  }

  private static void AddDistinct(List<string> values, string value)
  {
    if (!values.Contains(value, StringComparer.OrdinalIgnoreCase))
    {
      values.Add(value);
    }
  }
}
