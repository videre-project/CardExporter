/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CardExporter.MTGO.Parsing;


namespace CardExporter.MTGO.Records;

internal sealed record CardRecord(
  int Id,
  string? SetCode,
  string? Name,
  string? CollectorNumber,
  int? ArtId,
  string? Artist,
  int? CardTextureNumber,
  int? OtherFaceTextureNumber,
  IReadOnlyList<int> SplitCardIds,
  int? SplitParentCardId,
  int? SplitOtherCardId,
  IReadOnlyList<string> Colors,
  IReadOnlyList<string> ColorIdentity,
  decimal? ManaValue,
  string? FlavorText,
  string? ManaCost,
  string? TypeLine,
  string? OracleText,
  IReadOnlyList<string> Supertypes,
  IReadOnlyList<string> CardTypes,
  IReadOnlyList<string> Subtypes,
  string? Power,
  string? Toughness,
  string? Loyalty,
  string? Defense,
  string? Rarity,
  int? FrameStyle,
  string? PromoLabel,
  bool? HasActivatedAbility,
  bool? ShouldWork,
  bool? IsFoil,
  bool? IsToken,
  string RawJson
)
{
  public Guid OracleId => CreateOracleId();

  public int ColorMask => CreateColorMask(Colors);

  public int ColorIdentityMask => CreateColorMask(ColorIdentity);

  public bool HasMinimumIdentity => Name is not null || SetCode is not null;

  public static CardRecord? Create(
    DigitalObjectFields fields,
    LookupTables lookups,
    IReadOnlyDictionary<int, CardRecord> knownCards,
    IReadOnlySet<int> knownNonCards
  )
  {
    if (fields.CatalogId is not int catalogId)
    {
      return null;
    }

    if (fields.IsNonCardObject)
    {
      return null;
    }

    if (fields.IsFoilClone)
    {
      return null;
    }

    if (fields.CloneId is int baseCatalogId && knownNonCards.Contains(baseCatalogId))
    {
      return null;
    }

    string? name = lookups.ResolveCardName(fields);
    string? setCode = lookups.ResolveSetCode(fields);
    string? collectorNumber = fields.CollectorNumber;
    int? collectorNumberValue = fields.CollectorNumberValue;
    int? artId = fields.ArtId;
    string? artist = lookups.GetArtist(fields.ArtistId);
    int? cardTextureNumber = fields.CardTextureNumber;
    int? otherFaceTextureNumber = fields.OtherFaceTextureNumber;
    List<int> splitCardIds = fields.SplitCardCatalogIds.ToList();
    int? splitParentCardId = fields.SplitParentCatalogId;
    int? splitOtherCardId = fields.SplitOtherCardCatalogId;
    IReadOnlyList<string> colors = lookups.ResolveColors(fields.ColorId);
    IReadOnlyList<string> colorIdentity = lookups.ResolveColorIdentity(fields.ColorIdentityId);
    int? convertedManaCost = lookups.ResolveConvertedManaCost(fields.ConvertedManaCostId);
    decimal? manaValue = convertedManaCost;
    string? flavorText = lookups.GetFlavorText(fields.FlavorTextId);
    string? manaCost = lookups.GetManaCost(fields.ManaCostId);
    string? oracleText = lookups.GetOracleText(fields.OracleTextId);
    string? promoLabel = lookups.GetPromoLabel(fields.PromoLabelId);
    string? rarity = lookups.ResolveRarity(fields.RarityId);
    string? power = fields.Power;
    string? toughness = fields.Toughness;
    string? loyalty = lookups.GetLoyalty(fields.LoyaltyId);
    string? defense = lookups.GetDefense(fields.DefenseId);
    int? frameStyle = fields.FrameStyle;
    bool? hasActivatedAbility = fields.HasActivatedAbility;
    bool? shouldWork = fields.ShouldWork;
    bool? isFoil = fields.IsFoil;
    bool? isToken = fields.IsToken;
    IReadOnlyList<string> supertypes = fields.Supertypes.ToList();
    IReadOnlyList<string> cardTypes = fields.CardTypes.ToList();
    IReadOnlyList<string> subtypes = lookups.ResolveSubtypeNames(fields);
    string? typeLine;

    fields.ApplyPowerToughness(lookups, ref power, ref toughness);

    var cardDefinition = CardAdapter.CreateCard(
      catalogId,
      name,
      colors,
      convertedManaCost,
      manaCost,
      oracleText,
      supertypes.Concat(cardTypes),
      subtypes,
      artist,
      artId,
      collectorNumber,
      collectorNumberValue,
      flavorText,
      power,
      toughness,
      loyalty,
      defense,
      isToken,
      rarity
    );

    colors = CardAdapter.SplitColorDisplayString(cardDefinition.Colors);
    if (convertedManaCost is not null)
    {
      convertedManaCost = cardDefinition.ConvertedManaCost;
      manaValue = convertedManaCost;
    }

    flavorText = CardAdapter.NullIfEmpty(cardDefinition.FlavorText);
    manaCost = CardAdapter.NullIfEmpty(cardDefinition.ManaCost);
    typeLine = CardAdapter.BuildTypeLine(cardDefinition.Types, cardDefinition.Subtypes);
    oracleText = CardAdapter.NullIfEmpty(cardDefinition.RulesText);
    artist = CardAdapter.NullIfEmpty(cardDefinition.Artist);
    if (artId is not null)
    {
      artId = cardDefinition.ArtId;
    }

    collectorNumber = CardAdapter.NullIfEmpty(cardDefinition.CollectorInfo);

    power = CardAdapter.NullIfEmpty(cardDefinition.Power);
    toughness = CardAdapter.NullIfEmpty(cardDefinition.Toughness);
    loyalty = CardAdapter.NullIfEmpty(cardDefinition.Loyalty);
    defense = CardAdapter.NullIfEmpty(cardDefinition.Defense);
    if (isToken is not null)
    {
      isToken = cardDefinition.IsToken;
    }

    rarity = CardAdapter.NullIfEmpty(cardDefinition.Rarity);
    subtypes = cardDefinition.Subtypes.ToList();

    var card = new CardRecord(
      Id: catalogId,
      SetCode: setCode,
      Name: name,
      CollectorNumber: collectorNumber,
      ArtId: artId,
      Artist: artist,
      CardTextureNumber: cardTextureNumber,
      OtherFaceTextureNumber: otherFaceTextureNumber,
      SplitCardIds: splitCardIds,
      SplitParentCardId: splitParentCardId,
      SplitOtherCardId: splitOtherCardId,
      Colors: colors,
      ColorIdentity: colorIdentity,
      ManaValue: manaValue,
      FlavorText: flavorText,
      ManaCost: manaCost,
      TypeLine: typeLine,
      OracleText: oracleText,
      Supertypes: supertypes,
      CardTypes: cardTypes,
      Subtypes: subtypes,
      Power: power,
      Toughness: toughness,
      Loyalty: loyalty,
      Defense: defense,
      Rarity: rarity,
      FrameStyle: frameStyle,
      PromoLabel: promoLabel,
      HasActivatedAbility: hasActivatedAbility,
      ShouldWork: shouldWork,
      IsFoil: isFoil,
      IsToken: isToken,
      RawJson: JsonSerializer.Serialize(fields)
    );

    if (fields.CloneId is int baseCardCatalogId && knownCards.TryGetValue(baseCardCatalogId, out CardRecord? baseCard))
    {
      card = card.InheritMissingValuesFrom(baseCard);
    }

    return card.HasMinimumIdentity ? card : null;
  }

  public CardRecord InheritMissingValuesFrom(CardRecord baseCard)
  {
    return this with
    {
      Name = Name ?? baseCard.Name,
      SetCode = SetCode ?? baseCard.SetCode,
      CollectorNumber = CollectorNumber ?? baseCard.CollectorNumber,
      ArtId = ArtId ?? baseCard.ArtId,
      Artist = Artist ?? baseCard.Artist,
      CardTextureNumber = CardTextureNumber ?? baseCard.CardTextureNumber,
      OtherFaceTextureNumber = OtherFaceTextureNumber ?? baseCard.OtherFaceTextureNumber,
      SplitCardIds = SplitCardIds.Count == 0 ? baseCard.SplitCardIds.ToList() : SplitCardIds,
      SplitParentCardId = SplitParentCardId ?? baseCard.SplitParentCardId,
      SplitOtherCardId = SplitOtherCardId ?? baseCard.SplitOtherCardId,
      Colors = Colors.Count == 0 ? baseCard.Colors : Colors,
      ColorIdentity = ColorIdentity.Count == 0 ? baseCard.ColorIdentity : ColorIdentity,
      ManaValue = ManaValue ?? baseCard.ManaValue,
      FlavorText = FlavorText ?? baseCard.FlavorText,
      ManaCost = ManaCost ?? baseCard.ManaCost,
      TypeLine = TypeLine ?? baseCard.TypeLine,
      OracleText = OracleText ?? baseCard.OracleText,
      Supertypes = Supertypes.Count == 0 ? baseCard.Supertypes : Supertypes,
      CardTypes = CardTypes.Count == 0 ? baseCard.CardTypes : CardTypes,
      Subtypes = Subtypes.Count == 0 ? baseCard.Subtypes : Subtypes,
      Power = Power ?? baseCard.Power,
      Toughness = Toughness ?? baseCard.Toughness,
      Loyalty = Loyalty ?? baseCard.Loyalty,
      Defense = Defense ?? baseCard.Defense,
      Rarity = Rarity ?? baseCard.Rarity,
      FrameStyle = FrameStyle ?? baseCard.FrameStyle,
      PromoLabel = PromoLabel ?? baseCard.PromoLabel,
      HasActivatedAbility = HasActivatedAbility ?? baseCard.HasActivatedAbility,
      ShouldWork = ShouldWork ?? baseCard.ShouldWork,
      IsFoil = IsFoil ?? baseCard.IsFoil,
      IsToken = IsToken ?? baseCard.IsToken
    };
  }

  public static int CreateColorMask(IEnumerable<string> colors)
  {
    int mask = 0;
    foreach (string color in colors)
    {
      mask |= color.ToUpperInvariant() switch
      {
        "W" or "WHITE" => 1,
        "U" or "BLUE" => 2,
        "B" or "BLACK" => 4,
        "R" or "RED" => 8,
        "G" or "GREEN" => 16,
        _ => 0
      };
    }

    return mask;
  }

  private Guid CreateOracleId()
  {
    string key = string.Join(
      "\n",
      NormalizeText(Name),
      NormalizeText(ManaCost),
      ManaValue?.ToString("0.###", CultureInfo.InvariantCulture) ?? string.Empty,
      NormalizeText(TypeLine),
      NormalizeText(OracleText),
      NormalizeText(Power),
      NormalizeText(Toughness),
      NormalizeText(Loyalty),
      NormalizeText(Defense),
      NormalizeSymbols(Colors),
      NormalizeSymbols(ColorIdentity),
      NormalizeSymbols(Supertypes),
      NormalizeSymbols(CardTypes),
      NormalizeSymbols(Subtypes),
      IsToken == true ? "token" : "not-token"
    );

    byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
    byte[] bytes = hash.Take(16).ToArray();
    bytes[7] = (byte)((bytes[7] & 0x0F) | 0x50);
    bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
    return new Guid(bytes);
  }

  private static string NormalizeText(string? value)
  {
    if (string.IsNullOrWhiteSpace(value))
    {
      return string.Empty;
    }

    string[] parts = value.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
    return string.Join(" ", parts).ToLowerInvariant();
  }

  private static string NormalizeSymbols(IEnumerable<string> values)
  {
    return string.Join(
      ",",
      values
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => NormalizeText(value))
        .Distinct(StringComparer.Ordinal)
        .OrderBy(value => value, StringComparer.Ordinal)
    );
  }
}
