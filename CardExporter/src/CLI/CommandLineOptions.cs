/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.IO;


namespace CardExporter.CLI;

internal enum CommandMode
{
  Inspect,
  Import,
  Schedule,
  ExportImages,
  R2Manifest,
  R2Upload,
  R2Delete,
  SyncImages,
  SyncAssets,
  ExportManaSymbols,
  ExportMTGOAssets
}

internal sealed record CommandLineOptions(
  CommandMode Mode,
  string? DataDirectory,
  bool StartClient,
  bool LogOn,
  string? ConnectionString,
  IReadOnlyList<string> DumpFiles,
  int DumpLines,
  IReadOnlyList<string> ListFilePatterns,
  int ListFileLimit,
  IReadOnlyList<int> FindCatalogIds,
  IReadOnlyList<string> FindTexts,
  int FindTextLimit,
  IReadOnlyList<string> FindLookupIds,
  IReadOnlyList<int> ParseCatalogIds,
  IReadOnlyList<LookupProbe> LookupProbes,
  string SourceManifestRoot,
  ImageExportOptions ImageExport,
  ManaSymbolExportOptions ManaSymbols,
  MTGOAssetExportOptions MTGOAssets,
  bool SyncImages,
  bool SyncAssets,
  bool Force,
  R2Options R2,
  ScheduleOptions Schedule
)
{
  public static CommandLineOptions Parse(string[] args)
  {
    CommandMode mode = CommandMode.Inspect;
    string? dataDirectory = null;
    bool startClient = false;
    bool logOn = false;
    string? connectionString = null;
    List<string> dumpFiles = new List<string>();
    int dumpLines = 40;
    List<string> listFilePatterns = new List<string>();
    int listFileLimit = 80;
    List<int> findCatalogIds = new List<int>();
    List<string> findTexts = new List<string>();
    int findTextLimit = 40;
    List<string> findLookupIds = new List<string>();
    List<int> parseCatalogIds = new List<int>();
    List<LookupProbe> lookupProbes = new List<LookupProbe>();
    bool syncImages = false;
    bool syncAssets = false;
    bool force = false;
    string scheduleTimeZone = Environment.GetEnvironmentVariable("CARDEXPORTER_SCHEDULE_TIME_ZONE") ??
      ScheduleOptions.DefaultTimeZoneId;
    string scheduleWindows = Environment.GetEnvironmentVariable("CARDEXPORTER_SCHEDULE_WINDOWS") ??
      ScheduleOptions.DefaultWindows;
    int schedulePollIntervalMinutes = ParseOptionalPositiveInt(
      Environment.GetEnvironmentVariable("CARDEXPORTER_SCHEDULE_POLL_MINUTES"),
      "CARDEXPORTER_SCHEDULE_POLL_MINUTES",
      ScheduleOptions.DefaultPollIntervalMinutes
    );
    string sourceManifestRoot = Environment.GetEnvironmentVariable("CARDEXPORTER_SOURCE_MANIFEST_ROOT") ??
      SourceManifestOptions.DefaultSourceManifestRoot;
    string outputRoot = Environment.GetEnvironmentVariable("EXPORT_OUTPUT_ROOT") ?? ImageExportOptions.DefaultOutputRoot;
    string manaSymbolOutputRoot = Environment.GetEnvironmentVariable("CARDEXPORTER_MANA_SYMBOL_OUTPUT_ROOT") ??
      Path.Combine(outputRoot, "mana-symbols");
    string assetOutputRoot = Environment.GetEnvironmentVariable("CARDEXPORTER_ASSET_OUTPUT_ROOT") ??
      Path.Combine(outputRoot, "assets");
    string? mtgoAppDirectory = Environment.GetEnvironmentVariable("CARDEXPORTER_MTGO_APP_DIR");
    string cdnManifestPath = Environment.GetEnvironmentVariable("CARDEXPORTER_CDN_MANIFEST") ??
      Environment.GetEnvironmentVariable("CARDEXPORTER_IMAGE_MANIFEST") ??
      R2Options.DefaultCdnManifestPath;
    string r2Bucket = Environment.GetEnvironmentVariable("R2_BUCKET_NAME") ?? R2Options.DefaultBucketName;
    string r2EndpointUrl = Environment.GetEnvironmentVariable("R2_ENDPOINT_URL") ?? R2Options.DefaultEndpointUrl;
    string r2PublicBaseUrl = Environment.GetEnvironmentVariable("R2_PUBLIC_BASE_URL") ?? R2Options.DefaultPublicBaseUrl;
    bool dryRun = ParseOptionalBool(
      Environment.GetEnvironmentVariable("CARDEXPORTER_DRY_RUN"),
      "CARDEXPORTER_DRY_RUN"
    );
    int cardHeight = ParseOptionalPositiveInt(
      Environment.GetEnvironmentVariable("EXPORT_CARD_HEIGHT"),
      "EXPORT_CARD_HEIGHT",
      ImageExportOptions.DefaultCardHeight
    );
    string? setCode = Environment.GetEnvironmentVariable("EXPORT_SET_CODE");
    List<int> imageCatalogIds = new List<int>();
    HashSet<int> seenImageCatalogIds = new HashSet<int>();
    AddCatalogIds(
      imageCatalogIds,
      seenImageCatalogIds,
      Environment.GetEnvironmentVariable("EXPORT_CATALOG_IDS"),
      "EXPORT_CATALOG_IDS"
    );

    for (int i = 0; i < args.Length; i++)
    {
      string arg = args[i];
      if (string.Equals(arg, "inspect", StringComparison.OrdinalIgnoreCase))
      {
        mode = CommandMode.Inspect;
        continue;
      }

      if (string.Equals(arg, "import", StringComparison.OrdinalIgnoreCase))
      {
        mode = CommandMode.Import;
        continue;
      }

      if (string.Equals(arg, "schedule", StringComparison.OrdinalIgnoreCase) ||
          string.Equals(arg, "watch", StringComparison.OrdinalIgnoreCase))
      {
        mode = CommandMode.Schedule;
        continue;
      }

      if (string.Equals(arg, "export-images", StringComparison.OrdinalIgnoreCase) ||
          string.Equals(arg, "image-export", StringComparison.OrdinalIgnoreCase))
      {
        mode = CommandMode.ExportImages;
        continue;
      }

      if (string.Equals(arg, "r2-manifest", StringComparison.OrdinalIgnoreCase))
      {
        mode = CommandMode.R2Manifest;
        continue;
      }

      if (string.Equals(arg, "r2-upload", StringComparison.OrdinalIgnoreCase))
      {
        mode = CommandMode.R2Upload;
        continue;
      }

      if (string.Equals(arg, "r2-delete", StringComparison.OrdinalIgnoreCase))
      {
        mode = CommandMode.R2Delete;
        continue;
      }

      if (string.Equals(arg, "sync-images", StringComparison.OrdinalIgnoreCase))
      {
        mode = CommandMode.SyncImages;
        continue;
      }

      if (string.Equals(arg, "sync-assets", StringComparison.OrdinalIgnoreCase) ||
          string.Equals(arg, "sync-mtgo-assets", StringComparison.OrdinalIgnoreCase))
      {
        mode = CommandMode.SyncAssets;
        continue;
      }

      if (string.Equals(arg, "export-mana-symbols", StringComparison.OrdinalIgnoreCase) ||
          string.Equals(arg, "mana-symbol-export", StringComparison.OrdinalIgnoreCase))
      {
        mode = CommandMode.ExportManaSymbols;
        continue;
      }

      if (string.Equals(arg, "export-mtgo-assets", StringComparison.OrdinalIgnoreCase) ||
          string.Equals(arg, "export-symbol-assets", StringComparison.OrdinalIgnoreCase) ||
          string.Equals(arg, "mtgo-asset-export", StringComparison.OrdinalIgnoreCase))
      {
        mode = CommandMode.ExportMTGOAssets;
        continue;
      }

      if (string.Equals(arg, "--start-client", StringComparison.OrdinalIgnoreCase))
      {
        startClient = true;
        continue;
      }

      if (string.Equals(arg, "--log-on", StringComparison.OrdinalIgnoreCase))
      {
        startClient = true;
        logOn = true;
        continue;
      }

      if (string.Equals(arg, "--sync-images", StringComparison.OrdinalIgnoreCase))
      {
        syncImages = true;
        syncAssets = true;
        continue;
      }

      if (string.Equals(arg, "--sync-assets", StringComparison.OrdinalIgnoreCase))
      {
        syncAssets = true;
        continue;
      }

      if (string.Equals(arg, "--dry-run", StringComparison.OrdinalIgnoreCase))
      {
        dryRun = true;
        continue;
      }

      if (string.Equals(arg, "--force", StringComparison.OrdinalIgnoreCase))
      {
        force = true;
        continue;
      }

      if (TryReadOptionValue(args, ref i, "--data-dir", out string? dataDirOption))
      {
        dataDirectory = dataDirOption;
        continue;
      }

      if (TryReadOptionValue(args, ref i, "--dump-file", out string? dumpFile))
      {
        dumpFiles.Add(dumpFile);
        continue;
      }

      if (TryReadOptionValue(args, ref i, "--connection-string", out string? connectionStringOption))
      {
        connectionString = connectionStringOption;
        continue;
      }

      if (TryReadOptionValue(args, ref i, "--source-manifest-root", out string? sourceManifestRootOption))
      {
        sourceManifestRoot = sourceManifestRootOption;
        continue;
      }

      if (TryReadOptionValue(args, ref i, "--image-manifest", out string? imageManifestOption))
      {
        cdnManifestPath = imageManifestOption;
        continue;
      }

      if (TryReadOptionValue(args, ref i, "--cdn-manifest", out string? cdnManifestOption))
      {
        cdnManifestPath = cdnManifestOption;
        continue;
      }

      if (TryReadOptionValue(args, ref i, "--r2-bucket", out string? r2BucketOption))
      {
        r2Bucket = r2BucketOption;
        continue;
      }

      if (TryReadOptionValue(args, ref i, "--r2-endpoint-url", out string? r2EndpointUrlOption))
      {
        r2EndpointUrl = r2EndpointUrlOption;
        continue;
      }

      if (TryReadOptionValue(args, ref i, "--r2-public-base-url", out string? r2PublicBaseUrlOption))
      {
        r2PublicBaseUrl = r2PublicBaseUrlOption;
        continue;
      }

      if (TryReadOptionValue(args, ref i, "--schedule-time-zone", out string? scheduleTimeZoneOption))
      {
        scheduleTimeZone = scheduleTimeZoneOption;
        continue;
      }

      if (TryReadOptionValue(args, ref i, "--schedule-windows", out string? scheduleWindowsOption))
      {
        scheduleWindows = scheduleWindowsOption;
        continue;
      }

      if (TryReadOptionValue(args, ref i, "--poll-interval-minutes", out string? pollIntervalMinutesOption) ||
          TryReadOptionValue(args, ref i, "--poll-minutes", out pollIntervalMinutesOption))
      {
        schedulePollIntervalMinutes = ParsePositiveInt(
          pollIntervalMinutesOption,
          "--poll-interval-minutes"
        );
        continue;
      }

      if (TryReadOptionValue(args, ref i, "--dump-lines", out string? dumpLinesOption))
      {
        dumpLines = ParsePositiveInt(dumpLinesOption, "--dump-lines");
        continue;
      }

      if (TryReadOptionValue(args, ref i, "--list-files", out string? listFilesOption))
      {
        listFilePatterns.Add(listFilesOption);
        continue;
      }

      if (TryReadOptionValue(args, ref i, "--list-limit", out string? listLimitOption))
      {
        listFileLimit = ParsePositiveInt(listLimitOption, "--list-limit");
        continue;
      }

      if (TryReadOptionValue(args, ref i, "--find-catalog-id", out string? catalogIdOption))
      {
        findCatalogIds.Add(ParsePositiveInt(catalogIdOption, "--find-catalog-id"));
        continue;
      }

      if (TryReadOptionValue(args, ref i, "--find-text", out string? findTextOption))
      {
        findTexts.Add(findTextOption);
        continue;
      }

      if (TryReadOptionValue(args, ref i, "--find-text-limit", out string? findTextLimitOption))
      {
        findTextLimit = ParsePositiveInt(findTextLimitOption, "--find-text-limit");
        continue;
      }

      if (TryReadOptionValue(args, ref i, "--find-lookup-id", out string? lookupIdOption))
      {
        findLookupIds.Add(lookupIdOption);
        continue;
      }

      if (TryReadOptionValue(args, ref i, "--parse-catalog-id", out string? parseCatalogIdOption))
      {
        parseCatalogIds.Add(ParsePositiveInt(parseCatalogIdOption, "--parse-catalog-id"));
        continue;
      }

      if (TryReadOptionValue(args, ref i, "--probe-lookup", out string? probeLookupOption))
      {
        lookupProbes.Add(ParseLookupProbe(probeLookupOption));
        continue;
      }

      if (TryReadOptionValue(args, ref i, "--output-root", out string? outputRootOption))
      {
        outputRoot = outputRootOption;
        manaSymbolOutputRoot = outputRootOption;
        assetOutputRoot = outputRootOption;
        continue;
      }

      if (TryReadOptionValue(args, ref i, "--mtgo-app-dir", out string? mtgoAppDirOption))
      {
        mtgoAppDirectory = mtgoAppDirOption;
        continue;
      }

      if (TryReadOptionValue(args, ref i, "--card-height", out string? cardHeightOption))
      {
        cardHeight = ParsePositiveInt(cardHeightOption, "--card-height");
        continue;
      }

      if (TryReadOptionValue(args, ref i, "--set", out string? setCodeOption))
      {
        setCode = setCodeOption;
        continue;
      }

      if (TryReadOptionValue(args, ref i, "--catalog-id", out string? imageCatalogIdOption))
      {
        AddCatalogIds(imageCatalogIds, seenImageCatalogIds, imageCatalogIdOption, "--catalog-id");
        continue;
      }

      if (TryReadOptionValue(args, ref i, "--catalog-ids", out string? catalogIdsOption))
      {
        AddCatalogIds(imageCatalogIds, seenImageCatalogIds, catalogIdsOption, "--catalog-ids");
        continue;
      }

      throw new ArgumentException($"Unknown argument '{arg}'.");
    }

    if (mode == CommandMode.Import && !dryRun)
    {
      startClient = true;
    }

    if (mode == CommandMode.Schedule)
    {
      syncImages = true;
      syncAssets = true;
    }

    if (mode == CommandMode.ExportImages)
    {
      startClient = true;
      logOn = true;
    }

    if ((mode == CommandMode.SyncImages || (mode == CommandMode.Import && syncImages)) && !dryRun)
    {
      startClient = true;
      logOn = true;
    }

    if (imageCatalogIds.Count > 0 && !string.IsNullOrWhiteSpace(setCode))
    {
      throw new ArgumentException("--catalog-ids and --set are mutually exclusive.");
    }

    return new CommandLineOptions(
      mode,
      dataDirectory,
      startClient,
      logOn,
      connectionString,
      dumpFiles,
      dumpLines,
      listFilePatterns,
      listFileLimit,
      findCatalogIds,
      findTexts,
      findTextLimit,
      findLookupIds,
      parseCatalogIds,
      lookupProbes,
      sourceManifestRoot,
      new ImageExportOptions(
        outputRoot,
        cardHeight,
        ImageExportOptions.DefaultRenderColumns,
        imageCatalogIds,
        setCode
      ),
      new ManaSymbolExportOptions(
        manaSymbolOutputRoot,
        mtgoAppDirectory
      ),
      new MTGOAssetExportOptions(
        assetOutputRoot,
        mtgoAppDirectory
      ),
      syncImages,
      syncAssets,
      force,
      new R2Options(
        cdnManifestPath,
        outputRoot,
        r2Bucket,
        r2EndpointUrl,
        r2PublicBaseUrl,
        dryRun
      ),
      new ScheduleOptions(
        scheduleTimeZone,
        scheduleWindows,
        TimeSpan.FromMinutes(schedulePollIntervalMinutes)
      )
    );
  }

  private static bool TryReadOptionValue(
    string[] args,
    ref int index,
    string optionName,
    out string value
  )
  {
    value = string.Empty;
    string arg = args[index];
    if (string.Equals(arg, optionName, StringComparison.OrdinalIgnoreCase))
    {
      if (index + 1 >= args.Length)
      {
        throw new ArgumentException($"{optionName} requires a value.");
      }

      value = args[++index];
      return true;
    }

    string prefix = optionName + "=";
    if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
    {
      value = arg.Substring(prefix.Length);
      return true;
    }

    return false;
  }

  private static LookupProbe ParseLookupProbe(string value)
  {
    int separator = value.IndexOf(':');
    if (separator <= 0 || separator == value.Length - 1)
    {
      throw new ArgumentException("--probe-lookup must be in FILE:ID format.");
    }

    return new LookupProbe(value[..separator], value[(separator + 1)..]);
  }

  private static int ParsePositiveInt(string value, string optionName)
  {
    if (!int.TryParse(value, out int parsed) || parsed <= 0)
    {
      throw new ArgumentException($"{optionName} must be a positive integer.");
    }

    return parsed;
  }

  private static int ParseOptionalPositiveInt(string? value, string optionName, int defaultValue)
  {
    if (string.IsNullOrWhiteSpace(value))
    {
      return defaultValue;
    }

    return ParsePositiveInt(value, optionName);
  }

  private static bool ParseOptionalBool(string? value, string optionName)
  {
    if (string.IsNullOrWhiteSpace(value))
    {
      return false;
    }

    if (bool.TryParse(value, out bool parsed))
    {
      return parsed;
    }

    if (string.Equals(value, "1", StringComparison.Ordinal) ||
        string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "on", StringComparison.OrdinalIgnoreCase))
    {
      return true;
    }

    if (string.Equals(value, "0", StringComparison.Ordinal) ||
        string.Equals(value, "no", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "off", StringComparison.OrdinalIgnoreCase))
    {
      return false;
    }

    throw new ArgumentException($"{optionName} must be true or false.");
  }

  private static void AddCatalogIds(
    ICollection<int> catalogIds,
    ISet<int> seenCatalogIds,
    string? rawValue,
    string optionName
  )
  {
    if (string.IsNullOrWhiteSpace(rawValue))
    {
      return;
    }

    string[] parts = rawValue.Split(
      [',', ';', ' ', '\t', '\r', '\n'],
      StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
    );

    foreach (string part in parts)
    {
      int catalogId = ParsePositiveInt(part, optionName);
      if (seenCatalogIds.Add(catalogId))
      {
        catalogIds.Add(catalogId);
      }
    }
  }
}

internal sealed record LookupProbe(string FileName, string LookupId);

internal static class SourceManifestOptions
{
  public static string DefaultSourceManifestRoot => Path.Combine(DefaultPath.Root, "manifests");
}

internal sealed record ImageExportOptions(
  string OutputRoot,
  int CardHeight,
  int RenderColumns,
  IReadOnlyList<int> CatalogIds,
  string? SetCode
)
{
  public static string DefaultOutputRoot => Path.Combine(DefaultPath.Root, "output");
  public const int DefaultCardHeight = 300;
  public const int DefaultRenderColumns = 5;
}

internal sealed record ManaSymbolExportOptions(
  string OutputRoot,
  string? MTGOAppDirectory
);

internal sealed record MTGOAssetExportOptions(
  string OutputRoot,
  string? MTGOAppDirectory
);

internal sealed record R2Options(
  string CdnManifestPath,
  string OutputRoot,
  string BucketName,
  string EndpointUrl,
  string PublicBaseUrl,
  bool DryRun
)
{
  public static string DefaultCdnManifestPath => Path.Combine(
    SourceManifestOptions.DefaultSourceManifestRoot,
    "mtgo-cdn.csv"
  );
  public const string DefaultBucketName = "mtgo-cdn";
  public const string DefaultEndpointUrl = "https://1b2fec28af61a80de6823b7ac8356b4d.r2.cloudflarestorage.com";
  public const string DefaultPublicBaseUrl = "https://r2.videreproject.com";
}

internal sealed record ScheduleOptions(
  string TimeZoneId,
  string Windows,
  TimeSpan PollInterval
)
{
  public const string DefaultTimeZoneId = "America/Los_Angeles";
  public const string DefaultWindows = "Tuesday=08:00-10:00;Wednesday=09:00-12:00";
  public const int DefaultPollIntervalMinutes = 5;
}

internal static class DefaultPath
{
  public static string Root => ResolveRoot();

  private static string ResolveRoot()
  {
    foreach (string searchRoot in EnumerateSearchRoots())
    {
      DirectoryInfo? directory = new DirectoryInfo(searchRoot);
      while (directory is not null)
      {
        if (IsProjectRoot(directory.FullName))
        {
          return directory.FullName;
        }

        directory = directory.Parent;
      }
    }

    return Environment.CurrentDirectory;
  }

  private static IEnumerable<string> EnumerateSearchRoots()
  {
    if (!string.IsNullOrWhiteSpace(Environment.CurrentDirectory))
    {
      yield return Environment.CurrentDirectory;
    }

    if (!string.IsNullOrWhiteSpace(AppContext.BaseDirectory))
    {
      yield return AppContext.BaseDirectory;
    }
  }

  private static bool IsProjectRoot(string path)
  {
    return File.Exists(Path.Combine(path, "Project.slnx")) ||
      File.Exists(Path.Combine(path, "CardExporter", "CardExporter.csproj"));
  }
}
