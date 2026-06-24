/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using CardExporter.MTGO.Files;


namespace CardExporter.MTGO.Parsing;

internal sealed class ValidationRuleSetReader
{
  private static readonly IReadOnlyDictionary<string, string> FormatCodesByGameStructure =
    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
      ["CSTANDARD"] = "standard",
      ["CMODERN"] = "modern",
      ["CPIONEER"] = "pioneer",
      ["CVINTAGE"] = "vintage",
      ["CLEGACY"] = "legacy",
      ["CPAUPER"] = "pauper",
      ["CPREMODERN"] = "premodern"
    };

  private readonly CardDataFileIndex _files;

  public ValidationRuleSetReader(CardDataFileIndex files)
  {
    _files = files;
  }

  public IEnumerable<FormatLegalityRuleSet> EnumerateFormatLegalityRuleSets()
  {
    foreach (string file in _files.EnumerateValidationRuleSetFiles())
    {
      string sourceFile = _files.GetRelativePath(file);
      XDocument document = XDocument.Load(file, LoadOptions.None);
      XElement? root = document.Root;
      if (root is null)
      {
        continue;
      }

      string? gameStructure = Attribute(root, "GameStructure");
      if (gameStructure is null ||
          !FormatCodesByGameStructure.TryGetValue(gameStructure, out string? formatCode))
      {
        continue;
      }

      string sourceRuleSetId = CreateRuleSetId(sourceFile, root);
      var setRules = new List<SetLegalityRule>();
      var cardNameStatuses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
      bool requiresCommonPrinting = false;

      foreach (XElement rule in root.Elements())
      {
        string ruleName = rule.Name.LocalName;
        if (string.Equals(ruleName, "SetLimitationRule", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(ruleName, "FilteredSetLimitationRule", StringComparison.OrdinalIgnoreCase))
        {
          IReadOnlyList<SetRangeToken> rangeTokens = ParseRangeTokens(Attribute(rule, "Range"));
          if (rangeTokens.Count > 0)
          {
            setRules.Add(new SetLegalityRule(rangeTokens, ParseSetTypes(rule)));
          }
        }
        else if (string.Equals(ruleName, "CardLimitationRule", StringComparison.OrdinalIgnoreCase))
        {
          string? status = Attribute(rule, "Error") switch
          {
            "CardBanned" => "banned",
            "CardRestricted" => "restricted",
            _ => null
          };
          if (status is null)
          {
            continue;
          }

          foreach (string cardName in ReadChildValues(rule, "LimitedItem"))
          {
            string normalizedCardName = NormalizeCardName(cardName);
            if (normalizedCardName.Length > 0)
            {
              cardNameStatuses[normalizedCardName] = PickMoreRestrictiveStatus(
                status,
                cardNameStatuses.GetValueOrDefault(normalizedCardName)
              );
            }
          }
        }
        else if (string.Equals(ruleName, "AttributeLimitationRule", StringComparison.OrdinalIgnoreCase) &&
                 string.Equals(Attribute(rule, "Error"), "CardNotLegal", StringComparison.OrdinalIgnoreCase) &&
                 string.Equals(Attribute(rule, "Attribute"), "RARITY_STATUS", StringComparison.OrdinalIgnoreCase))
        {
          requiresCommonPrinting = ReadChildValues(rule, "Value")
            .Any(static value =>
              string.Equals(value, "MA_RARITY_COMMON", StringComparison.OrdinalIgnoreCase) ||
              string.Equals(value, "MA_RARITY_BASIC_LAND", StringComparison.OrdinalIgnoreCase));
        }
      }

      yield return new FormatLegalityRuleSet(
        sourceRuleSetId,
        formatCode,
        setRules,
        cardNameStatuses,
        requiresCommonPrinting
      );
    }
  }

  private static string CreateRuleSetId(string sourceFile, XElement root)
  {
    return Attribute(root, "id") ??
      Attribute(root, "ID") ??
      Attribute(root, "Id") ??
      Attribute(root, "rulesetId") ??
      Attribute(root, "ruleSetId") ??
      sourceFile;
  }

  private static string? Attribute(XElement element, string name)
  {
    return element.Attributes()
      .FirstOrDefault(attribute => string.Equals(attribute.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))
      ?.Value;
  }

  private static IReadOnlyList<SetRangeToken> ParseRangeTokens(string? range)
  {
    if (string.IsNullOrWhiteSpace(range))
    {
      return [];
    }

    var tokens = new List<SetRangeToken>();
    foreach (string rawPart in range.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
      string part = rawPart;
      bool isExcluded = part.StartsWith('!');
      if (isExcluded)
      {
        part = part[1..].Trim();
      }

      if (part.Length == 0)
      {
        continue;
      }

      int rangeSeparator = part.IndexOf('-');
      if (rangeSeparator >= 0)
      {
        string? startSetCode = NullIfWhiteSpace(part[..rangeSeparator]);
        string? endSetCode = NullIfWhiteSpace(part[(rangeSeparator + 1)..]);
        tokens.Add(new SetRangeToken(isExcluded, IsRange: true, startSetCode, endSetCode));
      }
      else
      {
        tokens.Add(new SetRangeToken(isExcluded, IsRange: false, StartSetCode: part, EndSetCode: null));
      }
    }

    return tokens;
  }

  private static IReadOnlySet<string> ParseSetTypes(XElement rule)
  {
    return rule
      .Descendants()
      .Where(static element => string.Equals(element.Name.LocalName, "CardSetType", StringComparison.OrdinalIgnoreCase))
      .Select(static element => element.Value.Trim())
      .Where(static value => value.Length > 0)
      .ToHashSet(StringComparer.OrdinalIgnoreCase);
  }

  private static IEnumerable<string> ReadChildValues(XElement element, string childName)
  {
    return element
      .Elements()
      .Where(child => string.Equals(child.Name.LocalName, childName, StringComparison.OrdinalIgnoreCase))
      .Select(static child => child.Value.Trim())
      .Where(static value => value.Length > 0);
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

  internal static string NormalizeCardName(string? cardName)
  {
    if (string.IsNullOrWhiteSpace(cardName))
    {
      return string.Empty;
    }

    string[] parts = cardName.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
    return string.Join(" ", parts).ToLowerInvariant();
  }

  private static string? NullIfWhiteSpace(string value)
  {
    return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
  }
}
