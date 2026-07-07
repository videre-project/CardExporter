/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using CardExporter.Database.Postgres;
using CardExporter.Prices;
using Microsoft.Extensions.Logging;


namespace CardExporter.CLI;

internal static class PriceSyncCommand
{
  public static async Task<int> ExecuteAsync(
    string connectionString,
    PriceSyncOptions options,
    bool dryRun,
    ILogger logger
  )
  {
    if (string.IsNullOrWhiteSpace(options.Source))
    {
      logger.LogError("Price source cannot be empty.");
      return 2;
    }

    if (options.FromYear < PriceSyncOptions.DefaultFromYear)
    {
      logger.LogError("Price backfill from-year cannot be earlier than {DefaultFromYear}.", PriceSyncOptions.DefaultFromYear);
      return 2;
    }

    using var httpClient = new HttpClient
    {
      Timeout = TimeSpan.FromMinutes(10)
    };
    httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("VidereCardExporter/1.0");

    var writer = new PriceSyncWriter(connectionString, logger);

    byte[] definitionBytes = await DownloadAsync(httpClient, options.CardDefinitionsUrl, logger);
    using (var definitionStream = new MemoryStream(definitionBytes, writable: false))
    {
      var definitions = GoatBotsPriceArchiveReader.ReadDefinitions(definitionStream, options.Source, logger);
      PriceDefinitionImportResult definitionResult = await writer.ImportDefinitionsAsync(definitions, dryRun);
      logger.LogInformation(
        "{DryRunPrefix}Imported {ImportedDefinitionCount} GoatBots price definitions from {StagedDefinitionCount} staged rows; {UnknownDefinitionCount} unknown catalog IDs skipped.",
        dryRun ? "[dry-run] " : string.Empty,
        definitionResult.ImportedCount,
        definitionResult.StagedCount,
        definitionResult.UnknownCatalogIdCount
      );
    }

    if (options.Backfill)
    {
      int currentYear = DateTime.UtcNow.Year;
      for (int year = options.FromYear; year <= currentYear; year++)
      {
        string archiveUrl = string.Format(
          CultureInfo.InvariantCulture,
          options.YearlyPriceHistoryUrlTemplate,
          year
        );
        await ImportPriceArchiveAsync(httpClient, writer, archiveUrl, options.Source, dryRun, logger);
      }
    }
    else
    {
      await ImportPriceArchiveAsync(httpClient, writer, options.LatestPriceHistoryUrl, options.Source, dryRun, logger);
    }

    return 0;
  }

  private static async Task ImportPriceArchiveAsync(
    HttpClient httpClient,
    PriceSyncWriter writer,
    string archiveUrl,
    string source,
    bool dryRun,
    ILogger logger
  )
  {
    byte[] archiveBytes = await DownloadAsync(httpClient, archiveUrl, logger);
    using var archiveStream = new MemoryStream(archiveBytes, writable: false);
    PriceHistoryImportResult result = await writer.ImportPricesAsync(
      GoatBotsPriceArchiveReader.ReadPrices(archiveStream, source, logger),
      dryRun
    );

    logger.LogInformation(
      "{DryRunPrefix}Imported {ImportedPriceCount} GoatBots price rows from {StagedPriceCount} staged rows in {ArchiveUrl}; {UnknownPriceCatalogIdCount} unknown catalog IDs skipped.",
      dryRun ? "[dry-run] " : string.Empty,
      result.ImportedCount,
      result.StagedCount,
      archiveUrl,
      result.UnknownCatalogIdCount
    );
  }

  private static async Task<byte[]> DownloadAsync(HttpClient httpClient, string url, ILogger logger)
  {
    logger.LogInformation("Downloading {Url}.", url);
    using HttpResponseMessage response = await httpClient.GetAsync(url);
    response.EnsureSuccessStatusCode();
    byte[] bytes = await response.Content.ReadAsByteArrayAsync();
    logger.LogInformation("Downloaded {ByteCount} bytes from {Url}.", bytes.Length, url);
    return bytes;
  }
}
