/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using CardExporter.MTGO.Parsing;


namespace CardExporter.MTGO.Records;

internal sealed record CardFace(
  int CardId,
  short FaceIndex,
  int? SourceCatalogId,
  string? Name,
  IReadOnlyList<string> Colors,
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
  string? Artist,
  int? ArtId,
  string RawJson
)
{
  public int ColorMask => CardRecord.CreateColorMask(Colors);

  public static List<CardFace> CreateFaces(
    DigitalObjectFields fields,
    CardRecord card,
    LookupTables lookups,
    IReadOnlyDictionary<int, DigitalObjectFields> fieldsByCatalogId,
    IReadOnlyDictionary<int, DigitalObjectFields> fieldsByTextureNumber,
    IReadOnlyDictionary<int, List<DigitalObjectFields>> fieldsByCloneId,
    IReadOnlyDictionary<int, List<CardFace>> knownFaces
  )
  {
    if (fields.CloneId is int baseCatalogId && knownFaces.TryGetValue(baseCatalogId, out List<CardFace>? baseFaces))
    {
      return baseFaces
        .Select(face => CreateCloneFace(card, face, fields, fieldsByCloneId, lookups))
        .ToList();
    }

    var faces = new List<CardFace>();
    if (fields.SplitCardCatalogIds.Count > 0)
    {
      short faceIndex = 0;
      foreach (int splitCardCatalogId in fields.SplitCardCatalogIds)
      {
        if (!fieldsByCatalogId.TryGetValue(splitCardCatalogId, out DigitalObjectFields? splitFields))
        {
          continue;
        }

        faces.Add(FromFields(card.Id, faceIndex, splitFields, lookups));
        faceIndex++;
      }
    }
    else if (fields.OtherFaceTextureNumber is int otherFaceTextureNumber &&
             fieldsByTextureNumber.TryGetValue(otherFaceTextureNumber, out DigitalObjectFields? otherFaceFields))
    {
      faces.Add(FromCardRecord(card, faceIndex: 0));
      faces.Add(FromFields(card.Id, faceIndex: 1, otherFaceFields, lookups));
    }
    else if (fields.HiddenSubcardChildTextureNumber is int hiddenSubcardChildTextureNumber &&
             fieldsByTextureNumber.TryGetValue(hiddenSubcardChildTextureNumber, out DigitalObjectFields? hiddenSubcardFields) &&
             hiddenSubcardFields.IsSubcard)
    {
      faces.Add(FromCardRecord(card, faceIndex: 0));
      faces.Add(FromFields(card.Id, faceIndex: 1, hiddenSubcardFields, lookups));
    }

    return faces;
  }

  public static CardFace FromCardRecord(CardRecord card, short faceIndex)
  {
    return new CardFace(
      CardId: card.Id,
      FaceIndex: faceIndex,
      SourceCatalogId: card.Id,
      Name: card.Name,
      Colors: card.Colors,
      ManaValue: card.ManaValue,
      FlavorText: card.FlavorText,
      ManaCost: card.ManaCost,
      TypeLine: card.TypeLine,
      OracleText: card.OracleText,
      Supertypes: card.Supertypes,
      CardTypes: card.CardTypes,
      Subtypes: card.Subtypes,
      Power: card.Power,
      Toughness: card.Toughness,
      Loyalty: card.Loyalty,
      Defense: card.Defense,
      Artist: card.Artist,
      ArtId: card.ArtId,
      RawJson: card.RawJson
    );
  }

  private static CardFace FromFields(
    int catalogId,
    short faceIndex,
    DigitalObjectFields fields,
    LookupTables lookups
  )
  {
    string? name = lookups.ResolveCardName(fields);
    IReadOnlyList<string> colors = lookups.ResolveColors(fields.ColorId);
    int? convertedManaCost = lookups.ResolveConvertedManaCost(fields.ConvertedManaCostId);
    decimal? manaValue = convertedManaCost;
    string? flavorText = lookups.GetFlavorText(fields.FlavorTextId);
    string? manaCost = lookups.GetManaCost(fields.ManaCostId);
    string? oracleText = lookups.GetOracleText(fields.OracleTextId);
    IReadOnlyList<string> supertypes = fields.Supertypes.ToList();
    IReadOnlyList<string> cardTypes = fields.CardTypes.ToList();
    IReadOnlyList<string> subtypes = lookups.ResolveSubtypeNames(fields);
    string? power = fields.Power;
    string? toughness = fields.Toughness;
    string? loyalty = lookups.GetLoyalty(fields.LoyaltyId);
    string? defense = lookups.GetDefense(fields.DefenseId);
    string? artist = lookups.GetArtist(fields.ArtistId);
    int? artId = fields.ArtId;
    fields.ApplyPowerToughness(lookups, ref power, ref toughness);

    var faceDefinition = CardAdapter.CreateCard(
      fields.CatalogId ?? catalogId,
      name,
      colors,
      convertedManaCost,
      manaCost,
      oracleText,
      supertypes.Concat(cardTypes),
      subtypes,
      artist,
      artId,
      collectorInfo: null,
      collectorNumber: null,
      flavorText,
      power,
      toughness,
      loyalty,
      defense,
      isToken: null,
      rarity: null
    );

    colors = CardAdapter.SplitColorDisplayString(faceDefinition.Colors);
    if (convertedManaCost is not null)
    {
      manaValue = faceDefinition.ConvertedManaCost;
    }

    flavorText = CardAdapter.NullIfEmpty(faceDefinition.FlavorText);
    artist = CardAdapter.NullIfEmpty(faceDefinition.Artist);
    if (artId is not null)
    {
      artId = faceDefinition.ArtId;
    }

    subtypes = faceDefinition.Subtypes.ToList();

    return new CardFace(
      CardId: catalogId,
      FaceIndex: faceIndex,
      SourceCatalogId: fields.CatalogId,
      Name: name,
      Colors: colors,
      ManaValue: manaValue,
      FlavorText: flavorText,
      ManaCost: CardAdapter.NullIfEmpty(faceDefinition.ManaCost),
      TypeLine: CardAdapter.BuildTypeLine(faceDefinition.Types, faceDefinition.Subtypes),
      OracleText: CardAdapter.NullIfEmpty(faceDefinition.RulesText),
      Supertypes: supertypes,
      CardTypes: cardTypes,
      Subtypes: subtypes,
      Power: CardAdapter.NullIfEmpty(faceDefinition.Power),
      Toughness: CardAdapter.NullIfEmpty(faceDefinition.Toughness),
      Loyalty: CardAdapter.NullIfEmpty(faceDefinition.Loyalty),
      Defense: CardAdapter.NullIfEmpty(faceDefinition.Defense),
      Artist: artist,
      ArtId: artId,
      RawJson: JsonSerializer.Serialize(fields)
    );
  }

  private static CardFace CreateCloneFace(
    CardRecord card,
    CardFace baseFace,
    DigitalObjectFields cloneFields,
    IReadOnlyDictionary<int, List<DigitalObjectFields>> fieldsByCloneId,
    LookupTables lookups
  )
  {
    if (baseFace.SourceCatalogId == cloneFields.CloneId)
    {
      return FromCardRecord(card, baseFace.FaceIndex);
    }

    if (baseFace.SourceCatalogId is int baseSourceCatalogId &&
        TryFindFaceCloneFields(baseSourceCatalogId, cloneFields, fieldsByCloneId, out DigitalObjectFields? faceCloneFields))
    {
      return FromFields(card.Id, baseFace.FaceIndex, faceCloneFields, lookups)
        .InheritMissingValuesFrom(baseFace);
    }

    return baseFace with { CardId = card.Id };
  }

  private CardFace InheritMissingValuesFrom(CardFace baseFace)
  {
    return this with
    {
      Name = Name ?? baseFace.Name,
      Colors = Colors.Count == 0 ? baseFace.Colors : Colors,
      ManaValue = ManaValue ?? baseFace.ManaValue,
      FlavorText = FlavorText ?? baseFace.FlavorText,
      ManaCost = ManaCost ?? baseFace.ManaCost,
      TypeLine = TypeLine ?? baseFace.TypeLine,
      OracleText = OracleText ?? baseFace.OracleText,
      Supertypes = Supertypes.Count == 0 ? baseFace.Supertypes : Supertypes,
      CardTypes = CardTypes.Count == 0 ? baseFace.CardTypes : CardTypes,
      Subtypes = Subtypes.Count == 0 ? baseFace.Subtypes : Subtypes,
      Power = Power ?? baseFace.Power,
      Toughness = Toughness ?? baseFace.Toughness,
      Loyalty = Loyalty ?? baseFace.Loyalty,
      Defense = Defense ?? baseFace.Defense,
      Artist = Artist ?? baseFace.Artist,
      ArtId = ArtId ?? baseFace.ArtId
    };
  }

  private static bool TryFindFaceCloneFields(
    int baseSourceCatalogId,
    DigitalObjectFields cloneFields,
    IReadOnlyDictionary<int, List<DigitalObjectFields>> fieldsByCloneId,
    out DigitalObjectFields faceCloneFields
  )
  {
    faceCloneFields = null!;
    if (!fieldsByCloneId.TryGetValue(baseSourceCatalogId, out List<DigitalObjectFields>? cloneCandidates))
    {
      return false;
    }

    if (cloneFields.CardTextureNumber is int parentTextureNumber)
    {
      foreach (DigitalObjectFields candidate in cloneCandidates)
      {
        if (candidate.HiddenSubcardParentTextureNumber == parentTextureNumber)
        {
          faceCloneFields = candidate;
          return true;
        }
      }
    }

    faceCloneFields = cloneCandidates[0];
    return true;
  }
}
