/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Threading;
using System.Threading.Tasks;
using CardExporter.CLI;
using CardExporter.CLI.Inspection;
using CardExporter.Database.Postgres;
using CardExporter.MTGO;
using CardExporter.MTGO.Files;
using Microsoft.Extensions.Logging;
using MTGOSDK.Core.Security;
using MTGOSDK.Win32;


namespace CardExporter;

internal static class Program
{
  [STAThread]
  public static async Task<int> Main(string[] args)
  {
    using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
    {
      builder.AddConsole();
      builder.SetMinimumLevel(LogLevel.Information);
    });

    ILogger logger = loggerFactory.CreateLogger("CardExporter");
    TryLoadDotEnv(logger);

    CommandLineOptions options;
    try
    {
      options = CommandLineOptions.Parse(args);
    }
    catch (ArgumentException ex)
    {
      logger.LogError(ex.Message);
      return 1;
    }

    using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
    ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
    {
      eventArgs.Cancel = true;
      cancellationTokenSource.Cancel();
      logger.LogInformation("Stopping CardExporter...");
    };

    try
    {
      Console.CancelKeyPress += cancelHandler;
      if (options.Mode == CommandMode.Schedule)
      {
        return await ScheduleCommand.ExecuteAsync(
          options,
          loggerFactory,
          logger,
          cancellationTokenSource.Token
        );
      }

      return await ExecuteOnceAsync(options, loggerFactory, logger);
    }
    catch (Exception exception)
    {
      logger.LogError(exception, "CardExporter failed.");
      return 1;
    }
    finally
    {
      Console.CancelKeyPress -= cancelHandler;
    }
  }

  internal static async Task<int> ExecuteOnceAsync(
    CommandLineOptions options,
    ILoggerFactory loggerFactory,
    ILogger logger
  )
  {
    VersionManifestPreflight? versionManifestPreflight = null;
    IDisposable? client = null;
    try
    {
      if (options.Mode == CommandMode.Import)
      {
        versionManifestPreflight = await VersionManifestPreflight.CheckAsync(
          options.SourceManifestRoot,
          options.R2.DryRun,
          logger
        );
        if (versionManifestPreflight.ShouldSkip && !options.Force)
        {
          logger.LogInformation(
            "MTGO codebase is unchanged; skipping source-file, database, image, and asset checks."
          );
          await versionManifestPreflight.CommitAsync(logger);
          return 0;
        }
        if (versionManifestPreflight.ShouldSkip && options.Force)
        {
          logger.LogInformation(
            "MTGO codebase is unchanged but --force was specified; continuing anyway."
          );
        }

        if (versionManifestPreflight.Status == VersionManifestPreflightStatus.Changed)
        {
          logger.LogInformation(
            "MTGO version manifest changed; continuing with local source-file checks: {Reason}.",
            versionManifestPreflight.Reason
          );
        }
      }

      if (options.Mode == CommandMode.R2Manifest)
      {
        return await R2ManifestCommand.ExecuteAsync(options.R2, logger);
      }

      if (options.Mode == CommandMode.R2Upload)
      {
        return await R2UploadCommand.ExecuteAsync(
          options.R2,
          ImportCommand.ResolveConnectionString(options),
          logger
        );
      }

      if (options.Mode == CommandMode.R2Delete)
      {
        return await R2DeleteCommand.ExecuteAsync(
          options.R2,
          options.ImageExport.CatalogIds,
          ImportCommand.ResolveConnectionString(options),
          logger
        );
      }

      if (options.Mode == CommandMode.SyncImages)
      {
        string? connectionString = ImportCommand.ResolveConnectionString(options);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
          logger.LogError("A database connection string is required. Set CARDEXPORTER_DATABASE_URL or pass --connection-string.");
          return 5;
        }

        if (options.StartClient)
        {
          client = await BotClient.StartAsync(loggerFactory, logger, options.LogOn);
        }

        return await ImageSyncCommand.ExecuteAsync(connectionString, options.R2, logger);
      }

      if (options.Mode == CommandMode.SyncAssets)
      {
        return await AssetSyncCommand.ExecuteAsync(
          options.MTGOAssets,
          options.R2,
          options.SourceManifestRoot,
          logger
        );
      }

      if (options.Mode == CommandMode.ExportManaSymbols)
      {
        if (options.StartClient)
        {
          client = await BotClient.StartAsync(loggerFactory, logger, options.LogOn);
        }

        return await ManaSymbolExportCommand.ExecuteAsync(options.ManaSymbols, logger);
      }

      if (options.Mode == CommandMode.ExportMTGOAssets)
      {
        return await MTGOAssetExportCommand.ExecuteAsync(options.MTGOAssets, logger);
      }

      if (options.Mode == CommandMode.ExportImages)
      {
        if (options.StartClient)
        {
          client = await BotClient.StartAsync(loggerFactory, logger, options.LogOn);
        }

        return ImageExportCommand.Execute(options.ImageExport, logger);
      }

      string? dataDirectory = options.DataDirectory ?? Constants.MTGODataDirectory;
      if (string.IsNullOrWhiteSpace(dataDirectory) && options.StartClient)
      {
        client = await BotClient.StartAsync(loggerFactory, logger, options.LogOn);
        dataDirectory = options.DataDirectory ?? Constants.MTGODataDirectory;
      }

      if (string.IsNullOrWhiteSpace(dataDirectory))
      {
        logger.LogError("MTGODataDirectory could not be resolved. Start MTGO once, or pass --data-dir.");
        return 2;
      }

      ImportPipelineAction importAction = ImportPipelineAction.ImportCards;
      if (options.Mode == CommandMode.Import)
      {
        string? preflightConnectionString = ImportCommand.ResolveConnectionString(options);
        if (string.IsNullOrWhiteSpace(preflightConnectionString))
        {
          logger.LogError("A database connection string is required. Set CARDEXPORTER_DATABASE_URL or pass --connection-string.");
          return 5;
        }

        ImportPreflightResult preflight = await ImportCommand.CheckPreflightAsync(
          dataDirectory,
          preflightConnectionString,
          options,
          loggerFactory,
          logger
        );
        if (preflight.ShouldSkip && !options.Force)
        {
          if (!options.SyncImages)
          {
            return await CompleteImportRunAsync(
              await SyncAssetsIfRequestedAsync(options, logger),
              versionManifestPreflight,
              logger
            );
          }

          string? connectionString = ImportCommand.ResolveConnectionString(options);
          if (string.IsNullOrWhiteSpace(connectionString))
          {
            logger.LogError("A database connection string is required. Set CARDEXPORTER_DATABASE_URL or pass --connection-string.");
            return 5;
          }

          var pendingImageSyncEntries = await PendingImageSyncRepository.GetEntriesAsync(
            connectionString,
            createIfMissing: !options.R2.DryRun
          );
          if (pendingImageSyncEntries.Count == 0)
          {
            logger.LogInformation("No source changes or pending image sync work detected; skipping import.");
            return await CompleteImportRunAsync(
              await SyncAssetsIfRequestedAsync(options, logger),
              versionManifestPreflight,
              logger
            );
          }

          if (options.StartClient && client is null && !options.R2.DryRun)
          {
            client = await BotClient.StartAsync(loggerFactory, logger, options.LogOn);
          }

          int pendingImageSyncResult = await ImportCommand.SyncPendingImagesAsync(
            connectionString,
            options,
            logger
          );
          if (pendingImageSyncResult != 0)
          {
            return pendingImageSyncResult;
          }

          return await CompleteImportRunAsync(
            await SyncAssetsIfRequestedAsync(options, logger),
            versionManifestPreflight,
            logger
          );
        }

        importAction = preflight.Action;
      }

      if (options.Mode == CommandMode.Import && options.R2.DryRun)
      {
        logger.LogInformation(
          "Dry run: would run {ImportAction}; no database import or source manifest update will be performed.",
          importAction
        );
        if (!options.SyncImages)
        {
          return await CompleteImportRunAsync(
            await SyncAssetsIfRequestedAsync(options, logger),
            versionManifestPreflight,
            logger
          );
        }

        logger.LogInformation(
          "Dry run: changed image catalog IDs from this import can only be computed during the import; checking already-pending image sync work only."
        );
        int pendingImageSyncResult = await ImportCommand.SyncPendingImagesAsync(options, logger);
        if (pendingImageSyncResult != 0)
        {
          return pendingImageSyncResult;
        }

        return await CompleteImportRunAsync(
          await SyncAssetsIfRequestedAsync(options, logger),
          versionManifestPreflight,
          logger
        );
      }

      if (options.StartClient && client is null)
      {
        client = await BotClient.StartAsync(loggerFactory, logger, options.LogOn);
      }

      if (options.Mode == CommandMode.Import)
      {
        int importResult = await ImportCommand.ExecuteAsync(
          dataDirectory,
          importAction,
          options,
          loggerFactory,
          logger
        );
        if (importResult != 0 ||
            importAction != ImportPipelineAction.ImportLegalities)
        {
          return await CompleteImportRunAsync(importResult, versionManifestPreflight, logger);
        }

        if (!options.SyncImages)
        {
          return await CompleteImportRunAsync(
            await SyncAssetsIfRequestedAsync(options, logger),
            versionManifestPreflight,
            logger
          );
        }

        string? connectionString = ImportCommand.ResolveConnectionString(options);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
          logger.LogError("A database connection string is required. Set CARDEXPORTER_DATABASE_URL or pass --connection-string.");
          return 5;
        }

        var pendingImageSyncEntries = await PendingImageSyncRepository.GetEntriesAsync(connectionString);
        if (pendingImageSyncEntries.Count == 0)
        {
          logger.LogInformation("Legality import completed and no pending image sync work was found.");
          return await CompleteImportRunAsync(
            await SyncAssetsIfRequestedAsync(options, logger),
            versionManifestPreflight,
            logger
          );
        }

        if (options.StartClient && client is null && !options.R2.DryRun)
        {
          client = await BotClient.StartAsync(loggerFactory, logger, options.LogOn);
        }

        int pendingImageSyncResult = await ImportCommand.SyncPendingImagesAsync(
          connectionString,
          options,
          logger
        );
        if (pendingImageSyncResult != 0)
        {
          return pendingImageSyncResult;
        }

        return await CompleteImportRunAsync(
          await SyncAssetsIfRequestedAsync(options, logger),
          versionManifestPreflight,
          logger
        );
      }

      InspectionCommand.Run(dataDirectory, options, loggerFactory, logger);
      return 0;
    }
    finally
    {
      client?.Dispose();
    }
  }

  private static async Task<int> SyncAssetsIfRequestedAsync(
    CommandLineOptions options,
    ILogger logger
  )
  {
    if (!options.SyncAssets)
    {
      return 0;
    }

    return await AssetSyncCommand.ExecuteAsync(
      options.MTGOAssets,
      options.R2,
      options.SourceManifestRoot,
      logger
    );
  }

  private static async Task<int> CompleteImportRunAsync(
    int result,
    VersionManifestPreflight? versionManifestPreflight,
    ILogger logger
  )
  {
    if (result == 0 && versionManifestPreflight is not null)
    {
      await versionManifestPreflight.CommitAsync(logger);
    }

    return result;
  }

  private static void TryLoadDotEnv(ILogger logger)
  {
    try
    {
      DotEnv.LoadFile();
    }
    catch (Exception exception)
    {
      logger.LogDebug(exception, "No .env file was loaded.");
    }
  }
}
