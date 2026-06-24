/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;


namespace CardExporter.MTGO.Files;

internal sealed class VersionManifestPreflight
{
  public const string FileName = "mtgo-version.json";
  public const string DefaultEndpointUrl = "https://api.videreproject.com/mtgo/manifest";

  private static readonly JsonSerializerOptions s_jsonOptions = new JsonSerializerOptions
  {
    WriteIndented = true
  };

  private readonly string m_manifestPath;
  private readonly string m_payload;
  private readonly bool m_dryRun;
  private readonly bool m_shouldCommit;

  private VersionManifestPreflight(
    VersionManifestPreflightStatus status,
    string reason,
    string manifestPath,
    string? payload,
    bool dryRun,
    bool shouldCommit
  )
  {
    Status = status;
    Reason = reason;
    m_manifestPath = manifestPath;
    m_payload = payload ?? string.Empty;
    m_dryRun = dryRun;
    m_shouldCommit = shouldCommit;
  }

  public VersionManifestPreflightStatus Status { get; }
  public string Reason { get; }
  public bool ShouldSkip => Status == VersionManifestPreflightStatus.Unchanged;
  public bool ShouldCommit => m_shouldCommit && !string.IsNullOrWhiteSpace(m_payload);

  public static async Task<VersionManifestPreflight> CheckAsync(
    string manifestRoot,
    bool dryRun,
    ILogger logger,
    CancellationToken cancellationToken = default
  )
  {
    string manifestPath = Path.Combine(manifestRoot, FileName);
    string endpointUrl = Environment.GetEnvironmentVariable("CARDEXPORTER_MTGO_VERSION_MANIFEST_URL") ??
      DefaultEndpointUrl;

    VersionManifestPayload remoteManifest;
    try
    {
      remoteManifest = await FetchAsync(endpointUrl, cancellationToken);
    }
    catch (Exception exception) when (exception is HttpRequestException ||
                                      exception is JsonException ||
                                      exception is IOException ||
                                      exception is InvalidDataException ||
                                      exception is TaskCanceledException)
    {
      logger.LogWarning(
        exception,
        "Could not fetch MTGO version manifest from {EndpointUrl}; falling back to local source-file checks.",
        endpointUrl
      );
      return new VersionManifestPreflight(
        VersionManifestPreflightStatus.Unavailable,
        $"MTGO version manifest could not be fetched from {endpointUrl}",
        manifestPath,
        null,
        dryRun,
        shouldCommit: false
      );
    }

    if (!File.Exists(manifestPath))
    {
      return new VersionManifestPreflight(
        VersionManifestPreflightStatus.Changed,
        $"MTGO version manifest was not found at {manifestPath}",
        manifestPath,
        remoteManifest.CanonicalJson,
        dryRun,
        shouldCommit: true
      );
    }

    VersionManifestPayload localManifest;
    try
    {
      localManifest = ParsePayload(await File.ReadAllTextAsync(manifestPath, cancellationToken));
    }
    catch (Exception exception) when (exception is JsonException ||
                                      exception is IOException ||
                                      exception is InvalidDataException)
    {
      logger.LogWarning(
        exception,
        "Could not read existing MTGO version manifest {ManifestPath}; it will be replaced after a successful run.",
        manifestPath
      );
      return new VersionManifestPreflight(
        VersionManifestPreflightStatus.Changed,
        $"MTGO version manifest could not be read at {manifestPath}",
        manifestPath,
        remoteManifest.CanonicalJson,
        dryRun,
        shouldCommit: true
      );
    }

    if (string.Equals(localManifest.Codebase, remoteManifest.Codebase, StringComparison.Ordinal))
    {
      bool payloadChanged = !string.Equals(
        localManifest.CanonicalJson,
        remoteManifest.CanonicalJson,
        StringComparison.Ordinal
      );
      return new VersionManifestPreflight(
        VersionManifestPreflightStatus.Unchanged,
        $"MTGO codebase {remoteManifest.Codebase} matches the saved manifest",
        manifestPath,
        remoteManifest.CanonicalJson,
        dryRun,
        shouldCommit: payloadChanged
      );
    }

    return new VersionManifestPreflight(
      VersionManifestPreflightStatus.Changed,
      $"MTGO codebase changed from {localManifest.Codebase} to {remoteManifest.Codebase}",
      manifestPath,
      remoteManifest.CanonicalJson,
      dryRun,
      shouldCommit: true
    );
  }

  public async Task CommitAsync(ILogger logger, CancellationToken cancellationToken = default)
  {
    if (!ShouldCommit)
    {
      return;
    }

    if (m_dryRun)
    {
      logger.LogInformation(
        "Dry run: would write MTGO version manifest to {ManifestPath}.",
        m_manifestPath
      );
      return;
    }

    string? directory = Path.GetDirectoryName(m_manifestPath);
    if (!string.IsNullOrWhiteSpace(directory))
    {
      Directory.CreateDirectory(directory);
    }

    string temporaryPath = m_manifestPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
    await File.WriteAllTextAsync(temporaryPath, m_payload, cancellationToken);
    File.Move(temporaryPath, m_manifestPath, overwrite: true);
    logger.LogInformation("Wrote MTGO version manifest to {ManifestPath}.", m_manifestPath);
  }

  private static async Task<VersionManifestPayload> FetchAsync(
    string endpointUrl,
    CancellationToken cancellationToken
  )
  {
    using var client = new HttpClient
    {
      Timeout = TimeSpan.FromSeconds(15)
    };
    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

    using HttpResponseMessage response = await client.GetAsync(endpointUrl, cancellationToken);
    if (!response.IsSuccessStatusCode)
    {
      throw new HttpRequestException(
        $"MTGO version manifest returned HTTP {(int)response.StatusCode} {response.ReasonPhrase}."
      );
    }

    string payload = await response.Content.ReadAsStringAsync(cancellationToken);
    if (string.IsNullOrWhiteSpace(payload))
    {
      throw new InvalidDataException("MTGO version manifest response was empty.");
    }

    return ParsePayload(payload);
  }

  private static VersionManifestPayload ParsePayload(string json)
  {
    using JsonDocument document = JsonDocument.Parse(json);
    if (!document.RootElement.TryGetProperty("codebase", out JsonElement codebaseElement) ||
        codebaseElement.ValueKind != JsonValueKind.String)
    {
      throw new InvalidDataException("MTGO version manifest was missing the codebase field.");
    }

    string? codebase = codebaseElement.GetString();
    if (string.IsNullOrWhiteSpace(codebase))
    {
      throw new InvalidDataException("MTGO version manifest had an empty codebase field.");
    }

    string canonicalJson = JsonSerializer.Serialize(document.RootElement, s_jsonOptions) + Environment.NewLine;
    return new VersionManifestPayload(canonicalJson, codebase);
  }

  private sealed record VersionManifestPayload(
    string CanonicalJson,
    string Codebase
  );
}

internal enum VersionManifestPreflightStatus
{
  Unavailable,
  Changed,
  Unchanged
}
