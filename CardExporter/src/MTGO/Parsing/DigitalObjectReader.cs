/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;


namespace CardExporter.MTGO.Parsing;

internal static class DigitalObjectReader
{
  private static readonly XmlReaderSettings ReaderSettings = new()
  {
    DtdProcessing = DtdProcessing.Ignore,
    IgnoreComments = true,
    IgnoreWhitespace = true
  };

  public static List<DigitalObjectFields> ReadAll(string file)
  {
    var fields = new List<DigitalObjectFields>();
    using XmlReader reader = XmlReader.Create(file, ReaderSettings);
    string? currentSetCode = null;

    while (reader.Read())
    {
      if (reader.NodeType != XmlNodeType.Element)
      {
        continue;
      }

      if (reader.Name == "CardSet")
      {
        currentSetCode = reader.GetAttribute("id");
        continue;
      }

      if (reader.Name != "DigitalObject")
      {
        continue;
      }

      if (ReadCurrent(reader, currentSetCode) is DigitalObjectFields objectFields)
      {
        fields.Add(objectFields);
      }
    }

    return fields;
  }

  private static DigitalObjectFields? ReadCurrent(XmlReader reader, string? currentSetCode)
  {
    int? catalogId = TryParseCatalogId(reader.GetAttribute("DigitalObjectCatalogID"), out int parsedCatalogId)
      ? parsedCatalogId
      : null;

    string? cardNameId = null;
    string? cardNameTokenId = null;
    string? cardSetId = null;
    string? collectorNumber = null;
    string? objectType = null;
    int? artId = null;
    string? artistId = null;
    int? cloneId = null;
    int? cardTextureNumber = null;
    int? otherFaceTextureNumber = null;
    int? hiddenSubcardChildTextureNumber = null;
    int? hiddenSubcardParentTextureNumber = null;
    int? splitOtherCardCatalogId = null;
    string? colorId = null;
    string? colorIdentityId = null;
    string? convertedManaCostId = null;
    string? flavorTextId = null;
    string? manaCostId = null;
    string? oracleTextId = null;
    string? promoLabelId = null;
    string? rarityId = null;
    string? power = null;
    string? toughness = null;
    string? powerToughnessId = null;
    string? loyaltyId = null;
    string? defenseId = null;
    int? frameStyle = null;
    bool? hasActivatedAbility = null;
    bool? shouldWork = null;
    bool? isAdventure = null;
    bool? isFoil = null;
    bool? isToken = null;
    bool? isContainer = null;
    bool? isDigitalObject = null;
    bool? isPrecon = null;
    bool? isTradable = null;
    List<string> supertypes = new List<string>(2);
    List<string> cardTypes = new List<string>(4);
    List<string> subtypeIds = new List<string>(4);
    List<int> splitCardCatalogIds = new List<int>(2);
    int? splitParentCatalogId = null;
    int digitalObjectDepth = reader.Depth;

    if (!reader.IsEmptyElement)
    {
      while (reader.Read())
      {
        if (reader.NodeType == XmlNodeType.EndElement &&
            reader.Depth == digitalObjectDepth &&
            reader.Name == "DigitalObject")
        {
          break;
        }

        if (reader.NodeType != XmlNodeType.Element)
        {
          continue;
        }

        if (reader.Name == "ARTID" && TryParseInt(reader.GetAttribute("value"), out int parsedArtId))
        {
          artId = parsedArtId;
        }
        else if (reader.Name == "ARTIST_NAME_STRING")
        {
          artistId = reader.GetAttribute("id");
        }
        else if (reader.Name == "CARDNAME_STRING")
        {
          cardNameId = reader.GetAttribute("id");
        }
        else if (reader.Name == "CARDNAME_TOKEN")
        {
          cardNameTokenId = reader.GetAttribute("id");
        }
        else if (reader.Name == "CARDSETNAME_STRING")
        {
          cardSetId = reader.GetAttribute("id");
        }
        else if (reader.Name == "COLLECTOR_INFO_STRING")
        {
          collectorNumber = reader.GetAttribute("value");
        }
        else if (reader.Name == "COLOR")
        {
          colorId = reader.GetAttribute("id");
        }
        else if (reader.Name == "COLOR_IDENTITY")
        {
          colorIdentityId = reader.GetAttribute("id");
        }
        else if (reader.Name == "CONVERTED_MANA_COST")
        {
          convertedManaCostId = reader.GetAttribute("id");
        }
        else if (reader.Name == "DIGITAL_OBJECT_TYPE_CODE_STRING")
        {
          objectType = reader.GetAttribute("value");
        }
        else if (reader.Name == "CLONE_ID" && int.TryParse(reader.GetAttribute("value"), out int parsedCloneId))
        {
          cloneId = parsedCloneId;
        }
        else if (reader.Name == "CARDTEXTURE_NUMBER" && int.TryParse(reader.GetAttribute("value"), out int parsedTextureNumber))
        {
          cardTextureNumber = parsedTextureNumber;
        }
        else if (reader.Name == "FLAVORTEXT_STRING")
        {
          flavorTextId = reader.GetAttribute("id");
        }
        else if (reader.Name == "FRAMESTYLE" && TryParseInt(reader.GetAttribute("value"), out int parsedFrameStyle))
        {
          frameStyle = parsedFrameStyle;
        }
        else if (reader.Name == "HAS_ACTIVATED_ABILITY")
        {
          hasActivatedAbility = IsTruthy(reader);
        }
        else if (reader.Name == "HIDDEN_SUBCARD_CHILD_CTN" &&
                 int.TryParse(reader.GetAttribute("value"), out int parsedSubcardChildTextureNumber))
        {
          hiddenSubcardChildTextureNumber = parsedSubcardChildTextureNumber;
        }
        else if (reader.Name == "HIDDEN_SUBCARD_PARENT_CTN" &&
                 int.TryParse(reader.GetAttribute("value"), out int parsedSubcardParentTextureNumber))
        {
          hiddenSubcardParentTextureNumber = parsedSubcardParentTextureNumber;
        }
        else if (reader.Name == "SHOULDWORK")
        {
          shouldWork = IsTruthy(reader);
        }
        else if (reader.Name == "IS_ADVENTURE")
        {
          isAdventure = IsTruthy(reader);
        }
        else if (reader.Name == "IS_FOIL")
        {
          isFoil = IsTruthy(reader);
        }
        else if (reader.Name == "OTHER_FACE" && int.TryParse(reader.GetAttribute("value"), out int parsedOtherFaceTextureNumber))
        {
          otherFaceTextureNumber = parsedOtherFaceTextureNumber;
        }
        else if (reader.Name.StartsWith("SPLITCARD0", StringComparison.Ordinal) &&
                 int.TryParse(reader.GetAttribute("value"), out int parsedSplitCatalogId))
        {
          splitCardCatalogIds.Add(parsedSplitCatalogId);
        }
        else if (reader.Name == "SPLITCARD_PARENTCARD" &&
                 int.TryParse(reader.GetAttribute("value"), out int parsedSplitParentCatalogId))
        {
          splitParentCatalogId = parsedSplitParentCatalogId;
        }
        else if (reader.Name == "SPLITCARD_OTHERCARD" &&
                 int.TryParse(reader.GetAttribute("value"), out int parsedSplitOtherCatalogId))
        {
          splitOtherCardCatalogId = parsedSplitOtherCatalogId;
        }
        else if (reader.Name == "MANA_COST_STRING")
        {
          manaCostId = reader.GetAttribute("id");
        }
        else if (reader.Name == "REAL_ORACLETEXT_STRING")
        {
          oracleTextId = reader.GetAttribute("id");
        }
        else if (reader.Name == "PROMO_LABEL_STRING")
        {
          promoLabelId = reader.GetAttribute("id");
        }
        else if (reader.Name == "RARITY_STATUS")
        {
          rarityId = reader.GetAttribute("id");
        }
        else if (reader.Name == "POWER")
        {
          power = reader.GetAttribute("value");
        }
        else if (reader.Name == "TOUGHNESS")
        {
          toughness = reader.GetAttribute("value");
        }
        else if (reader.Name == "POWERTOUGHNESS_STRING")
        {
          powerToughnessId = reader.GetAttribute("id");
        }
        else if (reader.Name == "LOYALTY_STRING")
        {
          loyaltyId = reader.GetAttribute("id");
        }
        else if (reader.Name == "DEFENSE_STRING")
        {
          defenseId = reader.GetAttribute("id");
        }
        else if (reader.Name == "IS_TOKEN")
        {
          isToken = IsTruthy(reader);
        }
        else if (reader.Name == "IS_CONTAINER")
        {
          isContainer = IsTruthy(reader);
        }
        else if (reader.Name == "IS_DIGITALOBJECT")
        {
          isDigitalObject = IsTruthy(reader);
        }
        else if (reader.Name == "IS_PRECON")
        {
          isPrecon = IsTruthy(reader);
        }
        else if (reader.Name == "UNTRADABLE")
        {
          isTradable = !IsTruthy(reader);
        }
        else if (TryGetSupertype(reader, out string? supertype))
        {
          AddDistinct(supertypes, supertype);
        }
        else if (TryGetCardType(reader, out string? cardType))
        {
          AddDistinct(cardTypes, cardType);
        }
        else if (TryGetSubtypeId(reader.Name, reader.GetAttribute("id"), out string? subtypeId))
        {
          AddDistinct(subtypeIds, subtypeId);
        }
      }
    }

    return new DigitalObjectFields(
      CatalogId: catalogId,
      CurrentSetCode: currentSetCode,
      CardNameId: cardNameId,
      CardNameTokenId: cardNameTokenId,
      CardSetId: cardSetId,
      CollectorNumber: collectorNumber,
      ObjectType: objectType,
      ArtId: artId,
      ArtistId: artistId,
      CloneId: cloneId,
      CardTextureNumber: cardTextureNumber,
      OtherFaceTextureNumber: otherFaceTextureNumber,
      HiddenSubcardChildTextureNumber: hiddenSubcardChildTextureNumber,
      HiddenSubcardParentTextureNumber: hiddenSubcardParentTextureNumber,
      SplitCardCatalogIds: splitCardCatalogIds,
      SplitParentCatalogId: splitParentCatalogId,
      SplitOtherCardCatalogId: splitOtherCardCatalogId,
      ColorId: colorId,
      ColorIdentityId: colorIdentityId,
      ConvertedManaCostId: convertedManaCostId,
      FlavorTextId: flavorTextId,
      ManaCostId: manaCostId,
      OracleTextId: oracleTextId,
      PromoLabelId: promoLabelId,
      RarityId: rarityId,
      Power: power,
      Toughness: toughness,
      PowerToughnessId: powerToughnessId,
      LoyaltyId: loyaltyId,
      DefenseId: defenseId,
      FrameStyle: frameStyle,
      HasActivatedAbility: hasActivatedAbility,
      ShouldWork: shouldWork,
      IsAdventure: isAdventure,
      IsFoil: isFoil,
      IsToken: isToken,
      IsContainer: isContainer,
      IsDigitalObject: isDigitalObject,
      IsPrecon: isPrecon,
      IsTradable: isTradable,
      Supertypes: supertypes,
      CardTypes: cardTypes,
      SubtypeIds: subtypeIds
    );
  }

  private static bool TryGetSupertype(XmlReader reader, out string supertype)
  {
    supertype = reader.Name switch
    {
      "IS_BASIC" or "BASIC" => "Basic",
      "IS_LEGENDARY" => "Legendary",
      "IS_SNOW" or "SNOW" => "Snow",
      "IS_WORLD" or "WORLD" => "World",
      _ => string.Empty
    };
    return supertype.Length > 0 && IsTruthy(reader);
  }

  private static bool TryGetCardType(XmlReader reader, out string cardType)
  {
    cardType = reader.Name switch
    {
      "ARTIFACT" or "IS_ARTIFACT" => "Artifact",
      "BATTLE" or "IS_BATTLE" => "Battle",
      "CREATURE" or "IS_CREATURE" => "Creature",
      "ENCHANTMENT" or "IS_ENCHANTMENT" => "Enchantment",
      "INSTANT" or "IS_INSTANT" => "Instant",
      "LAND" or "IS_LAND" => "Land",
      "PLANESWALKER" or "IS_PLANESWALKER" => "Planeswalker",
      "SORCERY" or "IS_SORCERY" => "Sorcery",
      "KINDRED" or "IS_KINDRED" or "TRIBAL" or "IS_TRIBAL" => "Kindred",
      _ => string.Empty
    };
    return cardType.Length > 0 && IsTruthy(reader);
  }

  private static bool TryGetSubtypeId(string elementName, string? id, out string subtypeId)
  {
    subtypeId = string.Empty;
    if (string.IsNullOrWhiteSpace(id))
    {
      return false;
    }

    if (!elementName.StartsWith("ARTIFACT_TYPE_STRING", StringComparison.Ordinal) &&
        !elementName.StartsWith("CREATURE_TYPE_STRING", StringComparison.Ordinal) &&
        !elementName.StartsWith("LAND_TYPE_STRING", StringComparison.Ordinal) &&
        !elementName.StartsWith("SUBTYPE_STRING", StringComparison.Ordinal))
    {
      return false;
    }

    subtypeId = id;
    return true;
  }

  private static bool IsTruthy(XmlReader reader)
  {
    string? value = reader.GetAttribute("value");
    return value is null ||
      (!string.Equals(value, "0", StringComparison.OrdinalIgnoreCase) &&
       !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase));
  }

  private static void AddDistinct(List<string> values, string value)
  {
    if (!values.Contains(value, StringComparer.OrdinalIgnoreCase))
    {
      values.Add(value);
    }
  }

  private static bool TryParseCatalogId(string? value, out int catalogId)
  {
    catalogId = 0;
    if (string.IsNullOrWhiteSpace(value))
    {
      return false;
    }

    ReadOnlySpan<char> span = value.AsSpan();
    if (span.StartsWith("DOC_", StringComparison.OrdinalIgnoreCase))
    {
      span = span[4..];
    }

    return int.TryParse(span, out catalogId);
  }

  private static bool TryParseInt(string? value, out int result)
  {
    result = 0;
    return !string.IsNullOrWhiteSpace(value) && int.TryParse(value, out result);
  }
}
