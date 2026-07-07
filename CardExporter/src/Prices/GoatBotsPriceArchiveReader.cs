/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using Microsoft.Extensions.Logging;


namespace CardExporter.Prices;

internal static class GoatBotsPriceArchiveReader
{
  private const string PriceEntryPrefix = "price-history-";
  private const string PriceEntrySuffix = ".txt";

  public static IReadOnlyList<CatalogPriceDefinitionRecord> ReadDefinitions(
    Stream zipStream,
    string source,
    ILogger logger
  )
  {
    using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true);
    ZipArchiveEntry? entry = archive.GetEntry("card-definitions.txt");
    if (entry is null)
    {
      throw new InvalidDataException("GoatBots card definitions archive did not contain card-definitions.txt.");
    }

    var definitions = new List<CatalogPriceDefinitionRecord>();
    using Stream entryStream = entry.Open();
    using JsonDocument document = JsonDocument.Parse(entryStream);
    if (document.RootElement.ValueKind != JsonValueKind.Object)
    {
      throw new InvalidDataException("GoatBots card definitions root must be a JSON object.");
    }

    foreach (JsonProperty property in document.RootElement.EnumerateObject())
    {
      if (!int.TryParse(property.Name, NumberStyles.None, CultureInfo.InvariantCulture, out int catalogId))
      {
        logger.LogWarning("Skipping GoatBots definition with non-integer catalog ID {CatalogId}.", property.Name);
        continue;
      }

      JsonElement value = property.Value;
      definitions.Add(new CatalogPriceDefinitionRecord(
        source,
        catalogId,
        GetString(value, "name"),
        GetString(value, "cardset"),
        GetString(value, "rarity"),
        GetString(value, "version"),
        GetFoil(value),
        value.GetRawText()
      ));
    }

    return definitions;
  }

  public static IEnumerable<CatalogPriceRecord> ReadPrices(
    Stream zipStream,
    string source,
    ILogger logger
  )
  {
    using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true);
    foreach (ZipArchiveEntry entry in archive.Entries)
    {
      if (!TryParsePriceDate(entry.Name, out DateOnly priceDate))
      {
        logger.LogDebug("Skipping non-price GoatBots ZIP entry {EntryName}.", entry.Name);
        continue;
      }

      using Stream entryStream = entry.Open();
      using JsonDocument document = JsonDocument.Parse(entryStream);
      if (document.RootElement.ValueKind != JsonValueKind.Object)
      {
        throw new InvalidDataException($"GoatBots price entry {entry.Name} root must be a JSON object.");
      }

      foreach (JsonProperty property in document.RootElement.EnumerateObject())
      {
        if (!int.TryParse(property.Name, NumberStyles.None, CultureInfo.InvariantCulture, out int catalogId))
        {
          logger.LogWarning(
            "Skipping GoatBots price in {EntryName} with non-integer catalog ID {CatalogId}.",
            entry.Name,
            property.Name
          );
          continue;
        }

        if (!property.Value.TryGetDecimal(out decimal sellPrice))
        {
          logger.LogWarning(
            "Skipping GoatBots price in {EntryName} for catalog ID {CatalogId}; value is not numeric.",
            entry.Name,
            property.Name
          );
          continue;
        }

        yield return new CatalogPriceRecord(source, priceDate, catalogId, sellPrice);
      }
    }
  }

  public static bool TryParsePriceDate(string entryName, out DateOnly priceDate)
  {
    priceDate = default;
    if (!entryName.StartsWith(PriceEntryPrefix, StringComparison.OrdinalIgnoreCase) ||
        !entryName.EndsWith(PriceEntrySuffix, StringComparison.OrdinalIgnoreCase))
    {
      return false;
    }

    string rawDate = entryName.Substring(
      PriceEntryPrefix.Length,
      entryName.Length - PriceEntryPrefix.Length - PriceEntrySuffix.Length
    );
    return DateOnly.TryParseExact(
      rawDate,
      "yyyy-MM-dd",
      CultureInfo.InvariantCulture,
      DateTimeStyles.None,
      out priceDate
    );
  }

  private static string? GetString(JsonElement value, string propertyName)
  {
    if (!value.TryGetProperty(propertyName, out JsonElement property) ||
        property.ValueKind == JsonValueKind.Null ||
        property.ValueKind == JsonValueKind.Undefined)
    {
      return null;
    }

    return property.ValueKind == JsonValueKind.String ? property.GetString() : property.ToString();
  }

  private static bool? GetFoil(JsonElement value)
  {
    if (!value.TryGetProperty("foil", out JsonElement property) ||
        property.ValueKind == JsonValueKind.Null ||
        property.ValueKind == JsonValueKind.Undefined)
    {
      return null;
    }

    if (property.ValueKind == JsonValueKind.True)
    {
      return true;
    }

    if (property.ValueKind == JsonValueKind.False)
    {
      return false;
    }

    if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out int numericValue))
    {
      return numericValue != 0;
    }

    return null;
  }
}
