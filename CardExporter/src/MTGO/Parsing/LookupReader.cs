/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using Microsoft.Extensions.Logging;


namespace CardExporter.MTGO.Parsing;

internal sealed class LookupReader
{
  private static readonly XmlReaderSettings ReaderSettings = new()
  {
    DtdProcessing = DtdProcessing.Ignore,
    IgnoreComments = true,
    IgnoreWhitespace = true
  };

  private readonly string _cardDataSourceDirectory;
  private readonly ILogger _logger;
  private LookupTables? _tables;

  public LookupReader(string cardDataSourceDirectory, ILogger logger)
  {
    _cardDataSourceDirectory = cardDataSourceDirectory;
    _logger = logger;
  }

  public LookupTables Load()
  {
    if (_tables is not null)
    {
      return _tables;
    }

    _tables = new LookupTables(
      CardNames: LoadLookup("CARDNAME_STRING.xml"),
      CardNameTokens: LoadLookup("CARDNAME_TOKEN.xml"),
      SetCodes: LoadLookup("CARDSETNAME_STRING.xml"),
      Artists: LoadLookup("ARTIST_NAME_STRING.xml"),
      Colors: LoadLookup("COLOR.xml"),
      ColorIdentities: LoadLookup("COLOR_IDENTITY.xml"),
      ConvertedManaCosts: LoadLookup("CONVERTED_MANA_COST.xml"),
      FlavorTexts: LoadLookup("FLAVORTEXT_STRING.xml"),
      ManaCosts: LoadLookup("MANA_COST_STRING.xml"),
      OracleTexts: LoadLookup("REAL_ORACLETEXT_STRING.xml"),
      PromoLabels: LoadLookup("PROMO_LABEL_STRING.xml"),
      Rarities: LoadLookup("RARITY_STATUS.xml"),
      PowerToughnesses: LoadLookup("POWERTOUGHNESS_STRING.xml"),
      Loyalties: LoadLookup("LOYALTY_STRING.xml"),
      Defenses: LoadLookup("DEFENSE_STRING.xml"),
      TypeNames: LoadTypeLookups()
    );
    return _tables;
  }

  public string? DebugValue(string fileName, string lookupId)
  {
    Dictionary<string, string> lookup = LoadLookup(fileName);
    return lookup.TryGetValue(lookupId, out string? value) ? value : null;
  }

  private Dictionary<string, string> LoadLookup(string fileName, bool log = true)
  {
    string path = Path.Combine(_cardDataSourceDirectory, fileName);
    var lookup = new Dictionary<string, string>(StringComparer.Ordinal);

    using XmlReader reader = XmlReader.Create(path, ReaderSettings);
    while (reader.Read())
    {
      if (reader.NodeType != XmlNodeType.Element)
      {
        continue;
      }

      string? id = reader.GetAttribute("id");
      if (string.IsNullOrWhiteSpace(id))
      {
        continue;
      }

      string value = reader.IsEmptyElement
        ? string.Empty
        : ReadElementText(reader);
      UpsertLookupValue(lookup, id, value);
    }

    if (log)
    {
      _logger.LogInformation("Loaded {LookupCount} values from {LookupFile}.", lookup.Count, fileName);
    }

    return lookup;
  }

  private Dictionary<string, string> LoadTypeLookups()
  {
    var lookup = new Dictionary<string, string>(StringComparer.Ordinal);
    int fileCount = 0;

    foreach (string file in Directory.EnumerateFiles(_cardDataSourceDirectory, "*TYPE*STRING*.xml").Order(StringComparer.OrdinalIgnoreCase))
    {
      fileCount++;
      foreach ((string id, string value) in LoadLookup(Path.GetFileName(file), log: false))
      {
        UpsertLookupValue(lookup, id, value);
      }
    }

    _logger.LogInformation("Loaded {LookupCount} type/subtype values from {LookupFileCount} lookup files.", lookup.Count, fileCount);
    return lookup;
  }

  private static string ReadElementText(XmlReader reader)
  {
    int elementDepth = reader.Depth;
    var value = new StringBuilder();
    while (reader.Read())
    {
      if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == elementDepth)
      {
        break;
      }

      if (reader.NodeType == XmlNodeType.Text ||
          reader.NodeType == XmlNodeType.CDATA ||
          reader.NodeType == XmlNodeType.SignificantWhitespace ||
          reader.NodeType == XmlNodeType.Whitespace)
      {
        value.Append(reader.Value);
      }
    }

    return value.ToString();
  }

  private static void UpsertLookupValue(
    Dictionary<string, string> lookup,
    string id,
    string value
  )
  {
    if (!lookup.TryGetValue(id, out string? existingValue) ||
        string.IsNullOrWhiteSpace(existingValue) ||
        !string.IsNullOrWhiteSpace(value))
    {
      lookup[id] = value;
    }
  }
}
