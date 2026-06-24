/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CardExporter.Database.R2;
using CardExporter.MTGO.Rendering.Cards;
using Npgsql;


namespace CardExporter.Database.Postgres;

internal static class CardImageCatalogRepository
{
  public static async Task<MissingCardImageCatalog> GetCatalogIdsForSyncAsync(
    string connectionString,
    IReadOnlySet<int> manifestCatalogIds,
    CardImageSyncScope scope
  )
  {
    var missingEntries = new List<CardImageCatalogEntry>();
    var summaries = new Dictionary<string, MissingCardImageSetSummaryBuilder>();
    if (!scope.IncludeAllCards && scope.CatalogIds.Count == 0)
    {
      return new MissingCardImageCatalog(missingEntries, []);
    }

    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var command = new NpgsqlCommand(
      """
      WITH normalized_images AS (
        SELECT
          CASE
            WHEN c.raw->>'CloneId' ~ '^[0-9]+$'
              AND coalesce((c.raw->>'IsFoil')::boolean, false)
              AND NOT (coalesce(c.set_code, '') = ANY(@separateFoilCloneImageSetCodes))
            THEN (c.raw->>'CloneId')::integer
            ELSE c.id
          END AS image_catalog_id,
          'card' AS image_kind,
          c.set_code,
          s.name AS set_name,
          s.release_date
        FROM cards c
        LEFT JOIN sets s ON s.code = c.set_code

        UNION ALL

        SELECT
          CASE
            WHEN f.raw->>'CloneId' ~ '^[0-9]+$'
              AND coalesce((f.raw->>'IsFoil')::boolean, false)
              AND NOT (coalesce(c.set_code, '') = ANY(@separateFoilCloneImageSetCodes))
            THEN (f.raw->>'CloneId')::integer
            ELSE f.source_catalog_id
          END AS image_catalog_id,
          'card' AS image_kind,
          c.set_code,
          s.name AS set_name,
          s.release_date
        FROM card_faces f
        JOIN cards c ON c.id = f.card_id
        LEFT JOIN sets s ON s.code = c.set_code
        WHERE f.source_catalog_id IS NOT NULL

        UNION ALL

        SELECT
          p.id AS image_catalog_id,
          'product' AS image_kind,
          p.set_code,
          s.name AS set_name,
          s.release_date
        FROM products p
        LEFT JOIN sets s ON s.code = p.set_code
      ),
      filtered_images AS (
        SELECT *
        FROM normalized_images
        WHERE @includeAllCards
          OR image_catalog_id = ANY(@catalogIds)
      ),
      required_images AS (
        SELECT DISTINCT ON (image_catalog_id)
          image_catalog_id AS id,
          image_kind,
          set_code,
          set_name,
          release_date
        FROM filtered_images
        ORDER BY image_catalog_id, image_kind, set_code NULLS LAST
      )
      SELECT
        id,
        image_kind,
        set_code,
        set_name,
        release_date
      FROM required_images
      ORDER BY id;
      """,
      connection
    );
    command.Parameters.AddWithValue(
      "separateFoilCloneImageSetCodes",
      FoilCloneImagePolicy.SeparateFoilCloneImageSetCodes
    );
    command.Parameters.AddWithValue("includeAllCards", scope.IncludeAllCards);
    command.Parameters.AddWithValue("catalogIds", scope.CatalogIds.ToArray());
    await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
      int catalogId = reader.GetInt32(0);
      if (scope.IncludeExistingManifestRows || !manifestCatalogIds.Contains(catalogId))
      {
        CardImageKind kind = ParseImageKind(reader.GetString(1));
        missingEntries.Add(new CardImageCatalogEntry(catalogId, kind));
        string? setCode = reader.IsDBNull(2) ? null : reader.GetString(2);
        string summaryKey = string.IsNullOrWhiteSpace(setCode) ? string.Empty : setCode;
        if (!summaries.TryGetValue(summaryKey, out MissingCardImageSetSummaryBuilder? summary))
        {
          summary = new MissingCardImageSetSummaryBuilder(
            setCode,
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : DateOnly.FromDateTime(reader.GetDateTime(4))
          );
          summaries[summaryKey] = summary;
        }

        summary.Add(catalogId);
      }
    }

    return new MissingCardImageCatalog(
      missingEntries,
      summaries.Values
        .Select(static summary => summary.Build())
        .OrderByDescending(static summary => summary.MissingCount)
        .ThenBy(static summary => summary.SetCode, StringComparer.OrdinalIgnoreCase)
        .ToList()
    );
  }

  public static async Task<IReadOnlyDictionary<int, CardImageKind>> GetImageKindsAsync(
    string connectionString,
    IReadOnlyCollection<int> catalogIds
  )
  {
    if (catalogIds.Count == 0)
    {
      return new Dictionary<int, CardImageKind>();
    }

    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var command = new NpgsqlCommand(
      """
      SELECT
        requested.catalog_id,
        CASE WHEN p.id IS NULL THEN 'card' ELSE 'product' END AS image_kind
      FROM unnest(@catalogIds) AS requested(catalog_id)
      LEFT JOIN products p ON p.id = requested.catalog_id;
      """,
      connection
    );
    command.Parameters.AddWithValue("catalogIds", catalogIds.ToArray());

    var kinds = new Dictionary<int, CardImageKind>();
    await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
      kinds[reader.GetInt32(0)] = ParseImageKind(reader.GetString(1));
    }

    return kinds;
  }

  private static CardImageKind ParseImageKind(string value)
  {
    return string.Equals(value, "product", StringComparison.OrdinalIgnoreCase)
      ? CardImageKind.Product
      : CardImageKind.Card;
  }

  private sealed class MissingCardImageSetSummaryBuilder
  {
    private int _missingCount;
    private int _firstCatalogId = int.MaxValue;
    private int _lastCatalogId = int.MinValue;

    public MissingCardImageSetSummaryBuilder(
      string? setCode,
      string? setName,
      DateOnly? releaseDate
    )
    {
      SetCode = setCode;
      SetName = setName;
      ReleaseDate = releaseDate;
    }

    private string? SetCode { get; }
    private string? SetName { get; }
    private DateOnly? ReleaseDate { get; }

    public void Add(int catalogId)
    {
      _missingCount++;
      _firstCatalogId = Math.Min(_firstCatalogId, catalogId);
      _lastCatalogId = Math.Max(_lastCatalogId, catalogId);
    }

    public MissingCardImageSetSummary Build()
    {
      return new MissingCardImageSetSummary(
        SetCode,
        SetName,
        ReleaseDate,
        _missingCount,
        _firstCatalogId,
        _lastCatalogId
      );
    }
  }

}

internal sealed record MissingCardImageCatalog(
  IReadOnlyList<CardImageCatalogEntry> Entries,
  IReadOnlyList<MissingCardImageSetSummary> SetSummaries
)
{
  public IReadOnlyList<int> CatalogIds => Entries.Select(static entry => entry.CatalogId).ToList();
}

internal sealed record CardImageCatalogEntry(
  int CatalogId,
  CardImageKind Kind
);

internal sealed record MissingCardImageSetSummary(
  string? SetCode,
  string? SetName,
  DateOnly? ReleaseDate,
  int MissingCount,
  int FirstCatalogId,
  int LastCatalogId
);

internal sealed record CardImageSyncScope(
  bool IncludeExistingManifestRows,
  bool IncludeAllCards,
  IReadOnlySet<int> CatalogIds,
  bool ClearPendingOnSuccess,
  string Description
)
{
  public static CardImageSyncScope MissingOnly()
  {
    return new CardImageSyncScope(
      IncludeExistingManifestRows: false,
      IncludeAllCards: true,
      CatalogIds: new HashSet<int>(),
      ClearPendingOnSuccess: false,
      Description: "missing database images"
    );
  }

  public static CardImageSyncScope PendingAddedCatalogIds(IReadOnlySet<int> catalogIds)
  {
    HashSet<int> catalogIdSet = catalogIds.ToHashSet();
    return new CardImageSyncScope(
      IncludeExistingManifestRows: false,
      IncludeAllCards: false,
      CatalogIds: catalogIdSet,
      ClearPendingOnSuccess: true,
      Description: catalogIdSet.Count == 0
        ? "pending added card images"
        : $"{catalogIdSet.Count} pending added card images"
    );
  }

  public static CardImageSyncScope PendingModifiedCatalogIds(IReadOnlySet<int> catalogIds)
  {
    HashSet<int> catalogIdSet = catalogIds.ToHashSet();
    return new CardImageSyncScope(
      IncludeExistingManifestRows: true,
      IncludeAllCards: false,
      CatalogIds: catalogIdSet,
      ClearPendingOnSuccess: true,
      Description: catalogIdSet.Count == 0
        ? "pending modified card images"
        : $"{catalogIdSet.Count} pending modified card images"
    );
  }
}
