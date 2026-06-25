/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using CardExporter.MTGO.Files;
using CardExporter.MTGO.Parsing;
using CardExporter.MTGO.Records;
using Microsoft.Extensions.Logging;


namespace CardExporter.MTGO;

internal sealed class Parser
{
  private static readonly XmlReaderSettings ReaderSettings = new()
  {
    DtdProcessing = DtdProcessing.Ignore,
    IgnoreComments = true,
    IgnoreWhitespace = true
  };

  private readonly CardDataFileIndex _files;
  private readonly LookupReader _lookupReader;
  private readonly ValidationRuleSetReader _validationRuleSetReader;
  private readonly ILogger _logger;
  private readonly IReadOnlyDictionary<string, SetMetadata> _setMetadata;
  private IReadOnlyDictionary<string, string>? _productSetNamesByCode;
  private IReadOnlyList<ProductRecord>? _products;

  private static readonly IReadOnlyDictionary<string, string[]> RuleSetTypeAliases =
    new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
      ["ANCI"] = ["Ancillary"],
      ["CORE"] = ["CoreSet"],
      ["FXAN"] = ["FixedAncillary"],
      ["LARS"] = ["LargeExpansionSet"],
      ["PRMO"] = ["PromotionalSet"],
      ["SMLS"] = ["SmallExpansionSet"],
      ["SUPP"] = ["Supplemental"]
    };

  public Parser(
    string dataDirectory,
    ILogger logger,
    IReadOnlyDictionary<string, SetMetadata>? setMetadata = null
  )
  {
    _files = new CardDataFileIndex(dataDirectory, logger);
    _lookupReader = new LookupReader(_files.SourceDirectory, logger);
    _validationRuleSetReader = new ValidationRuleSetReader(_files);
    _logger = logger;
    _setMetadata = setMetadata ?? new Dictionary<string, SetMetadata>(StringComparer.OrdinalIgnoreCase);
  }

  public IEnumerable<SourceFile> EnumerateParsedSourceFiles()
  {
    return _files.EnumerateParsedSourceFiles();
  }

  public IEnumerable<SourceFile> EnumerateSharedSourceFiles()
  {
    return _files.EnumerateSharedSourceFiles();
  }

  public IEnumerable<SourceFile> EnumerateLegalitySourceFiles()
  {
    return _files.EnumerateLegalitySourceFiles();
  }

  public IEnumerable<CardLegality> EnumerateCardLegalities()
  {
    List<SetRecord> sets = EnumerateSets().ToList();
    List<CardRecord> cards = EnumerateCards().ToList();
    Dictionary<string, int> setAgesByCode = sets
      .Where(static set => set.Age is not null)
      .GroupBy(static set => set.Code, StringComparer.OrdinalIgnoreCase)
      .ToDictionary(static group => group.Key, static group => group.First().Age!.Value, StringComparer.OrdinalIgnoreCase);
    Dictionary<string, HashSet<Guid>> oracleIdsByCardName = BuildOracleIdsByCardName(cards);
    List<Guid> allOracleIds = cards
      .Select(static card => card.OracleId)
      .Distinct()
      .OrderBy(static oracleId => oracleId)
      .ToList();

    foreach (FormatLegalityRuleSet ruleSet in _validationRuleSetReader.EnumerateFormatLegalityRuleSets())
    {
      HashSet<string> legalSetCodes = ResolveLegalSetCodes(ruleSet, sets, setAgesByCode);
      HashSet<Guid> legalOracleIds = cards
        .Where(card => IsLegalPrinting(card, legalSetCodes, ruleSet.RequiresCommonPrinting))
        .Select(static card => card.OracleId)
        .ToHashSet();
      Dictionary<Guid, string> overrideStatuses = ResolveCardNameStatuses(ruleSet, oracleIdsByCardName);

      foreach (Guid oracleId in allOracleIds)
      {
        string status = overrideStatuses.TryGetValue(oracleId, out string? overrideStatus)
          ? overrideStatus
          : legalOracleIds.Contains(oracleId) ? "legal" : "not_legal";

        yield return new CardLegality(
          oracleId,
          ruleSet.FormatCode,
          status,
          ruleSet.SourceRuleSetId
        );
      }
    }
  }

  public string? DebugLookupValue(string fileName, string lookupId)
  {
    return _lookupReader.DebugValue(fileName, lookupId);
  }

  public IEnumerable<SetRecord> EnumerateSets()
  {
    IReadOnlyDictionary<string, string> productSetNamesByCode = GetProductSetNamesByCode();
    var emittedSetCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    foreach (string file in _files.GetSetFiles())
    {
      string sourceFile = _files.GetRelativePath(file);
      bool foundSet = false;
      using XmlReader reader = XmlReader.Create(file, ReaderSettings);
      while (reader.Read())
      {
        if (reader.NodeType != XmlNodeType.Element || reader.Name != "CardSet")
        {
          continue;
        }

        string? code = reader.GetAttribute("id");
        if (string.IsNullOrWhiteSpace(code))
        {
          _logger.LogWarning("Skipping CardSet without id in {File}", file);
          foundSet = true;
          break;
        }

        emittedSetCodes.Add(code);
        _setMetadata.TryGetValue(code, out SetMetadata? metadata);
        productSetNamesByCode.TryGetValue(code, out string? productSetName);

        yield return SetRecord.Create(
          code,
          reader.GetAttribute("age"),
          reader.GetAttribute("cardsetType"),
          sourceFile,
          metadata,
          productSetName
        );
        foundSet = true;
        break;
      }

      if (!foundSet)
      {
        _logger.LogWarning("No CardSet root was found in {File}", file);
      }
    }

    foreach (string code in GetProducts()
      .Select(static product => product.SetCode)
      .Where(static code => !string.IsNullOrWhiteSpace(code))
      .Select(static code => code!)
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .Order(StringComparer.OrdinalIgnoreCase))
    {
      if (!emittedSetCodes.Add(code))
      {
        continue;
      }

      _setMetadata.TryGetValue(code, out SetMetadata? metadata);
      productSetNamesByCode.TryGetValue(code, out string? productSetName);

      yield return SetRecord.Create(
        code,
        age: null,
        cardsetType: null,
        sourceFile: string.Empty,
        metadata,
        productSetName
      );
    }
  }

  private IReadOnlyDictionary<string, string> GetProductSetNamesByCode()
  {
    if (_productSetNamesByCode is not null)
    {
      return _productSetNamesByCode;
    }

    _productSetNamesByCode = SetNameResolver.InferFromProducts(GetProducts());
    return _productSetNamesByCode;
  }

  public IEnumerable<CardRecord> EnumerateCards()
  {
    LookupTables lookups = _lookupReader.Load();
    Dictionary<int, CardRecord> knownCards = new Dictionary<int, CardRecord>();
    HashSet<int> knownNonCards = new HashSet<int>();

    foreach (string file in _files.GetSetFiles())
    {
      List<DigitalObjectFields> fileFields = DigitalObjectReader.ReadAll(file);
      foreach (DigitalObjectFields fields in fileFields)
      {
        if (fields.CatalogId is int catalogId && fields.IsNonCardObject)
        {
          knownNonCards.Add(catalogId);
        }
      }

      foreach (DigitalObjectFields fields in OrderFieldsForCardCreation(fileFields))
      {
        CardRecord? card = CardRecord.Create(
          fields,
          lookups,
          knownCards,
          knownNonCards
        );
        if (card is not null)
        {
          knownCards[card.Id] = card;
          yield return card;
        }
      }
    }
  }

  public IEnumerable<ProductRecord> EnumerateProducts()
  {
    return GetProducts();
  }

  private IReadOnlyList<ProductRecord> GetProducts()
  {
    if (_products is not null)
    {
      return _products;
    }

    LookupTables lookups = _lookupReader.Load();

    _products = _files.GetSetFiles()
      .Concat(_files.GetProductFiles())
      .SelectMany(static file => DigitalObjectReader.ReadAll(file))
      .Select(fields => ProductRecord.Create(fields, lookups))
      .OfType<ProductRecord>()
      .ToArray();

    return _products;
  }

  public IEnumerable<CardCatalogVariantRecord> EnumerateCardCatalogVariants()
  {
    LookupTables lookups = _lookupReader.Load();
    Dictionary<int, CardRecord> knownCards = new Dictionary<int, CardRecord>();
    HashSet<int> knownNonCards = new HashSet<int>();

    foreach (string file in _files.GetSetFiles())
    {
      List<DigitalObjectFields> fileFields = DigitalObjectReader.ReadAll(file);

      foreach (DigitalObjectFields fields in fileFields)
      {
        if (fields.CatalogId is int catalogId && fields.IsNonCardObject)
        {
          knownNonCards.Add(catalogId);
        }
      }

      foreach (DigitalObjectFields fields in OrderFieldsForCardCreation(fileFields))
      {
        if (fields.CatalogId is not int catalogId)
        {
          continue;
        }

        if (fields.IsFoilClone)
        {
          if (fields.CloneId is int baseCatalogId &&
              knownCards.TryGetValue(baseCatalogId, out CardRecord? baseCard) &&
              CardCatalogVariantRecord.Create(fields, baseCard, lookups) is CardCatalogVariantRecord variant)
          {
            yield return variant;
          }

          continue;
        }

        CardRecord? card = CardRecord.Create(
          fields,
          lookups,
          knownCards,
          knownNonCards
        );
        if (card is not null)
        {
          knownCards[catalogId] = card;
        }
      }
    }
  }

  public IEnumerable<CardFace> EnumerateCardFaces()
  {
    LookupTables lookups = _lookupReader.Load();
    Dictionary<int, CardRecord> knownCards = new Dictionary<int, CardRecord>();
    Dictionary<int, List<CardFace>> knownFaces = new Dictionary<int, List<CardFace>>();
    HashSet<int> knownNonCards = new HashSet<int>();

    foreach (string file in _files.GetSetFiles())
    {
      List<DigitalObjectFields> fileFields = DigitalObjectReader.ReadAll(file);
      var fieldsByCatalogId = new Dictionary<int, DigitalObjectFields>();
      var fieldsByTextureNumber = new Dictionary<int, DigitalObjectFields>();
      var fieldsByCloneId = new Dictionary<int, List<DigitalObjectFields>>();

      foreach (DigitalObjectFields fields in fileFields)
      {
        if (fields.CatalogId is int catalogId && !fieldsByCatalogId.ContainsKey(catalogId))
        {
          fieldsByCatalogId[catalogId] = fields;
        }

        if (fields.CardTextureNumber is int textureNumber && !fieldsByTextureNumber.ContainsKey(textureNumber))
        {
          fieldsByTextureNumber[textureNumber] = fields;
        }

        if (fields.CloneId is int baseCatalogId)
        {
          if (!fieldsByCloneId.TryGetValue(baseCatalogId, out List<DigitalObjectFields>? cloneFields))
          {
            cloneFields = new List<DigitalObjectFields>();
            fieldsByCloneId[baseCatalogId] = cloneFields;
          }

          cloneFields.Add(fields);
        }
      }

      foreach (DigitalObjectFields fields in fileFields)
      {
        if (fields.CatalogId is int catalogId && fields.IsNonCardObject)
        {
          knownNonCards.Add(catalogId);
        }
      }

      foreach (DigitalObjectFields fields in OrderFieldsForCardCreation(fileFields))
      {
        if (fields.CatalogId is not int catalogId)
        {
          continue;
        }

        CardRecord? card = CardRecord.Create(
          fields,
          lookups,
          knownCards,
          knownNonCards
        );
        if (card is not null)
        {
          knownCards[catalogId] = card;
        }
      }

      foreach (DigitalObjectFields fields in fileFields)
      {
        if (fields.CatalogId is not int catalogId || !knownCards.TryGetValue(catalogId, out CardRecord? card))
        {
          continue;
        }

        List<CardFace> faces = CardFace.CreateFaces(
          fields,
          card,
          lookups,
          fieldsByCatalogId,
          fieldsByTextureNumber,
          fieldsByCloneId,
          knownFaces
        );
        knownFaces[catalogId] = faces;

        foreach (CardFace face in faces)
        {
          yield return face;
        }
      }
    }
  }

  public ParserDebugInfo? DebugCatalogId(int targetCatalogId)
  {
    LookupTables lookups = _lookupReader.Load();
    Dictionary<int, CardRecord> knownCards = new Dictionary<int, CardRecord>();
    HashSet<int> knownNonCards = new HashSet<int>();

    foreach (string file in _files.GetSetFiles())
    {
      foreach (DigitalObjectFields fields in DigitalObjectReader.ReadAll(file))
      {
        if (fields.CatalogId is int catalogId && fields.IsNonCardObject)
        {
          knownNonCards.Add(catalogId);
        }

        CardRecord? card = CardRecord.Create(
          fields,
          lookups,
          knownCards,
          knownNonCards
        );
        if (card is not null)
        {
          knownCards[card.Id] = card;
        }

        if (fields.CatalogId == targetCatalogId)
        {
          return new ParserDebugInfo(
            SourceFile: _files.GetRelativePath(file),
            Fields: fields,
            CardName: lookups.GetCardName(fields.CardNameId),
            CardNameToken: lookups.GetCardNameToken(fields.CardNameTokenId),
            SetCode: lookups.ResolveSetCode(fields),
            CardRecord: card
          );
        }
      }
    }

    return null;
  }

  private static Dictionary<string, HashSet<Guid>> BuildOracleIdsByCardName(IEnumerable<CardRecord> cards)
  {
    var oracleIdsByCardName = new Dictionary<string, HashSet<Guid>>(StringComparer.OrdinalIgnoreCase);
    foreach (CardRecord card in cards)
    {
      string normalizedName = ValidationRuleSetReader.NormalizeCardName(card.Name);
      if (normalizedName.Length == 0)
      {
        continue;
      }

      if (!oracleIdsByCardName.TryGetValue(normalizedName, out HashSet<Guid>? oracleIds))
      {
        oracleIds = new HashSet<Guid>();
        oracleIdsByCardName[normalizedName] = oracleIds;
      }

      oracleIds.Add(card.OracleId);
    }

    return oracleIdsByCardName;
  }

  private static Dictionary<Guid, string> ResolveCardNameStatuses(
    FormatLegalityRuleSet ruleSet,
    IReadOnlyDictionary<string, HashSet<Guid>> oracleIdsByCardName
  )
  {
    var statuses = new Dictionary<Guid, string>();
    foreach (KeyValuePair<string, string> item in ruleSet.CardNameStatuses)
    {
      if (!oracleIdsByCardName.TryGetValue(item.Key, out HashSet<Guid>? oracleIds))
      {
        continue;
      }

      foreach (Guid oracleId in oracleIds)
      {
        statuses[oracleId] = PickMoreRestrictiveStatus(item.Value, statuses.GetValueOrDefault(oracleId));
      }
    }

    return statuses;
  }

  private static HashSet<string> ResolveLegalSetCodes(
    FormatLegalityRuleSet ruleSet,
    IReadOnlyList<SetRecord> sets,
    IReadOnlyDictionary<string, int> setAgesByCode
  )
  {
    var legalSetCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (SetLegalityRule setRule in ruleSet.SetRules)
    {
      foreach (SetRecord set in sets)
      {
        if (MatchesIncludedRange(set, setRule, setAgesByCode) &&
            !MatchesExcludedRange(set, setRule.RangeTokens, setAgesByCode))
        {
          legalSetCodes.Add(set.Code);
        }
      }
    }

    return legalSetCodes;
  }

  private static bool MatchesIncludedRange(
    SetRecord set,
    SetLegalityRule setRule,
    IReadOnlyDictionary<string, int> setAgesByCode
  )
  {
    foreach (SetRangeToken token in setRule.RangeTokens)
    {
      if (token.IsExcluded || !MatchesRangeToken(set, token, setAgesByCode))
      {
        continue;
      }

      if (!token.IsRange || SetTypeMatches(set.SetType, setRule.SetTypes))
      {
        return true;
      }
    }

    return false;
  }

  private static bool MatchesExcludedRange(
    SetRecord set,
    IReadOnlyList<SetRangeToken> rangeTokens,
    IReadOnlyDictionary<string, int> setAgesByCode
  )
  {
    return rangeTokens.Any(token => token.IsExcluded && MatchesRangeToken(set, token, setAgesByCode));
  }

  private static bool MatchesRangeToken(
    SetRecord set,
    SetRangeToken token,
    IReadOnlyDictionary<string, int> setAgesByCode
  )
  {
    if (!token.IsRange)
    {
      return string.Equals(set.Code, token.StartSetCode, StringComparison.OrdinalIgnoreCase);
    }

    if (set.Age is not int setAge)
    {
      return false;
    }

    int minAge = int.MinValue;
    int maxAge = int.MaxValue;
    if (token.StartSetCode is not null)
    {
      if (!setAgesByCode.TryGetValue(token.StartSetCode, out minAge))
      {
        return false;
      }
    }

    if (token.EndSetCode is not null)
    {
      if (!setAgesByCode.TryGetValue(token.EndSetCode, out maxAge))
      {
        return false;
      }
    }

    if (minAge > maxAge)
    {
      (minAge, maxAge) = (maxAge, minAge);
    }

    return setAge >= minAge && setAge <= maxAge;
  }

  private static bool SetTypeMatches(string? setType, IReadOnlySet<string> ruleSetTypes)
  {
    if (ruleSetTypes.Count == 0)
    {
      return true;
    }

    if (setType is null)
    {
      return false;
    }

    foreach (string ruleSetType in ruleSetTypes)
    {
      if (string.Equals(setType, ruleSetType, StringComparison.OrdinalIgnoreCase))
      {
        return true;
      }

      if (!RuleSetTypeAliases.TryGetValue(ruleSetType, out string[]? aliases))
      {
        continue;
      }

      foreach (string alias in aliases)
      {
        if (string.Equals(setType, alias, StringComparison.OrdinalIgnoreCase))
        {
          return true;
        }
      }
    }

    return false;
  }

  private static IEnumerable<DigitalObjectFields> OrderFieldsForCardCreation(
    IEnumerable<DigitalObjectFields> fields
  )
  {
    return fields.OrderBy(static field => field.CloneId is not null);
  }

  private static bool IsLegalPrinting(
    CardRecord card,
    IReadOnlySet<string> legalSetCodes,
    bool requiresCommonPrinting
  )
  {
    if (card.SetCode is null ||
        !legalSetCodes.Contains(card.SetCode) ||
        card.IsToken == true ||
        card.ShouldWork == false)
    {
      return false;
    }

    return !requiresCommonPrinting || IsCommonOrBasicLand(card.Rarity);
  }

  private static bool IsCommonOrBasicLand(string? rarity)
  {
    return string.Equals(rarity, "common", StringComparison.OrdinalIgnoreCase) ||
      string.Equals(rarity, "basic land", StringComparison.OrdinalIgnoreCase);
  }

  private static string PickMoreRestrictiveStatus(string status, string? existingStatus)
  {
    if (existingStatus is null)
    {
      return status;
    }

    return StatusRank(status) > StatusRank(existingStatus) ? status : existingStatus;
  }

  private static int StatusRank(string status)
  {
    return status switch
    {
      "banned" => 3,
      "restricted" => 2,
      "legal" => 1,
      _ => 0
    };
  }
}
