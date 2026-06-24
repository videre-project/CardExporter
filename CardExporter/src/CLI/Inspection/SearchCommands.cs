/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;


namespace CardExporter.CLI.Inspection;

internal static class SearchCommands
{
  public static void FindCatalogId(string dataDirectory, int catalogId, ILogger logger)
  {
    string cardDataDirectory = Path.Combine(dataDirectory, "CardDataSource");
    string needle = $"DigitalObjectCatalogID=\"DOC_{catalogId}\"";
    logger.LogInformation("Searching for catalog ID {CatalogId}.", catalogId);

    foreach (string file in Directory.EnumerateFiles(cardDataDirectory, "client_*.xml").OrderBy(static f => f))
    {
      using var reader = new StreamReader(file);
      int lineNumber = 0;
      while (reader.ReadLine() is string line)
      {
        lineNumber++;
        if (!line.Contains(needle, StringComparison.Ordinal))
        {
          continue;
        }

        logger.LogInformation("Found catalog ID {CatalogId} in {File} at line {LineNumber}.", catalogId, Path.GetRelativePath(dataDirectory, file), lineNumber);
        logger.LogInformation("{LineNumber,4}: {Line}", lineNumber, line);

        for (int i = 0; i < 120 && reader.ReadLine() is string objectLine; i++)
        {
          lineNumber++;
          logger.LogInformation("{LineNumber,4}: {Line}", lineNumber, objectLine);
          if (objectLine.Contains("</DigitalObject>", StringComparison.Ordinal))
          {
            return;
          }
        }

        return;
      }
    }

    logger.LogWarning("Catalog ID {CatalogId} was not found.", catalogId);
  }

  public static void FindText(string dataDirectory, string text, int limit, ILogger logger)
  {
    string cardDataDirectory = Path.Combine(dataDirectory, "CardDataSource");
    logger.LogInformation("Searching for text '{Text}' in CardDataSource.", text);

    int matchCount = 0;
    foreach (string file in Directory.EnumerateFiles(cardDataDirectory, "*", SearchOption.TopDirectoryOnly).OrderBy(static f => f))
    {
      using var reader = new StreamReader(file);
      int lineNumber = 0;
      while (reader.ReadLine() is string line)
      {
        lineNumber++;
        if (!line.Contains(text, StringComparison.OrdinalIgnoreCase))
        {
          continue;
        }

        matchCount++;
        if (matchCount <= limit)
        {
          logger.LogInformation("{File}:{LineNumber}: {Line}", Path.GetRelativePath(dataDirectory, file), lineNumber, line);
        }
      }
    }

    logger.LogInformation("Found {MatchCount} matches for text '{Text}'.", matchCount, text);
  }

  public static void FindLookupId(string dataDirectory, string lookupId, ILogger logger)
  {
    string cardDataDirectory = Path.Combine(dataDirectory, "CardDataSource");
    logger.LogInformation("Searching for lookup ID {LookupId}.", lookupId);

    foreach (string file in Directory.EnumerateFiles(cardDataDirectory, "*.xml").OrderBy(static f => f))
    {
      using var reader = new StreamReader(file);
      int lineNumber = 0;
      while (reader.ReadLine() is string line)
      {
        lineNumber++;
        if (!line.Contains($"id=\"{lookupId}\"", StringComparison.Ordinal))
        {
          continue;
        }

        logger.LogInformation("{File}:{LineNumber}: {Line}", Path.GetRelativePath(dataDirectory, file), lineNumber, line);
        return;
      }
    }

    logger.LogWarning("Lookup ID {LookupId} was not found.", lookupId);
  }
}
