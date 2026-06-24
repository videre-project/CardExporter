/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;
using MTGOSDK.API.Graphics;


namespace CardExporter.MTGO.Rendering.Cards;

internal static class CardImageRenderer
{
  public const int DefaultBatchSize = 50;
  private const int MaxRenderAttempts = 3;
  private const int RenderRetryDelayMilliseconds = 500;

  public static RenderedCardBatch RenderCards(
    IReadOnlyList<int> catalogIds,
    int renderHeight,
    int renderColumns,
    ILogger logger
  )
  {
    ArtOverrideApplicator.ApplyForCatalogIds(catalogIds, renderHeight, renderColumns, logger);

    var renderedImagesByCatalogId = new Dictionary<int, RenderedCardImage>();
    var pendingCatalogIds = catalogIds.ToList();
    var lastFailureReasons = new Dictionary<int, string>();

    for (int attempt = 1; attempt <= MaxRenderAttempts && pendingCatalogIds.Count > 0; attempt++)
    {
      RenderAttemptResult attemptResult = RenderCardsOnce(
        pendingCatalogIds,
        renderHeight,
        renderColumns,
        logger
      );

      foreach (RenderedCardImage image in attemptResult.Images)
      {
        renderedImagesByCatalogId[image.CatalogId] = image;
        lastFailureReasons.Remove(image.CatalogId);
      }

      foreach ((int catalogId, string reason) in attemptResult.Failures)
      {
        lastFailureReasons[catalogId] = reason;
      }

      pendingCatalogIds = attemptResult.Failures.Keys.ToList();
      if (pendingCatalogIds.Count > 0 && attempt < MaxRenderAttempts)
      {
        logger.LogInformation(
          "Retrying {PendingCount} card image renders after attempt {Attempt}/{MaxAttempts}.",
          pendingCatalogIds.Count,
          attempt,
          MaxRenderAttempts
        );
        Thread.Sleep(RenderRetryDelayMilliseconds);
      }
    }

    foreach (int catalogId in pendingCatalogIds)
    {
      logger.LogWarning(
        "{FailureReason} for catalog ID {CatalogId} after {MaxAttempts} render attempts.",
        lastFailureReasons.TryGetValue(catalogId, out string? reason) ? reason : "No acceptable rendered image was returned",
        catalogId,
        MaxRenderAttempts
      );
    }

    List<RenderedCardImage> renderedImages = catalogIds
      .Where(renderedImagesByCatalogId.ContainsKey)
      .Select(catalogId => renderedImagesByCatalogId[catalogId])
      .ToList();
    return new RenderedCardBatch(renderedImages, pendingCatalogIds.Count);
  }

  private static RenderAttemptResult RenderCardsOnce(
    IReadOnlyList<int> catalogIds,
    int renderHeight,
    int renderColumns,
    ILogger logger
  )
  {
    try
    {
      int[] batchArray = new int[catalogIds.Count];
      for (int i = 0; i < catalogIds.Count; i++)
      {
        batchArray[i] = catalogIds[i];
      }

      string[] base64Cards = CardRenderer.RenderCards(
        batchArray,
        columns: renderColumns,
        cardHeight: renderHeight
      );

      int resultCount = Math.Min(base64Cards.Length, batchArray.Length);
      var renderedImages = new List<RenderedCardImage>(resultCount);
      var failures = new Dictionary<int, string>();

      for (int index = 0; index < resultCount; index++)
      {
        int catalogId = batchArray[index];
        if (!TryDecodeRenderedCard(base64Cards[index], out byte[]? imageBytes))
        {
          failures[catalogId] = "No rendered image was returned";
          continue;
        }

        if (!RenderedCardImageInspector.IsSupportedPng(imageBytes))
        {
          failures[catalogId] = "Rendered image was not a supported PNG";
          continue;
        }

        if (!RenderedCardImageInspector.TryReadDimensions(imageBytes, out RenderedCardImageInspector.PngDimensions dimensions) ||
            dimensions.Height != renderHeight)
        {
          failures[catalogId] = $"Rendered image height was {dimensions.Height}px instead of requested {renderHeight}px";
          continue;
        }

        if (RenderedCardImageInspector.HasLikelyMissingArt(imageBytes, out string diagnostic))
        {
          failures[catalogId] = $"Rendered image appears to be missing card art; rejecting image ({diagnostic})";
          continue;
        }

        renderedImages.Add(new RenderedCardImage(catalogId, imageBytes));
      }

      for (int index = resultCount; index < batchArray.Length; index++)
      {
        failures[batchArray[index]] = "No rendered image was returned";
      }

      return new RenderAttemptResult(renderedImages, failures);
    }
    catch (Exception renderException)
    {
      logger.LogError(renderException, "CardRenderer.RenderCards failed for a batch of {BatchSize} cards.", catalogIds.Count);
      var failures = new Dictionary<int, string>();
      foreach (int catalogId in catalogIds)
      {
        failures[catalogId] = "CardRenderer.RenderCards failed";
      }

      return new RenderAttemptResult([], failures);
    }
  }

  public static bool TryDecodeRenderedCard(string? renderedCard, [NotNullWhen(true)] out byte[]? imageBytes)
  {
    imageBytes = null;
    if (string.IsNullOrEmpty(renderedCard))
    {
      return false;
    }

    try
    {
      string base64String = renderedCard;
      int commaIndex = base64String.IndexOf(',');
      if (commaIndex >= 0)
      {
        base64String = base64String[(commaIndex + 1)..];
      }

      imageBytes = Convert.FromBase64String(base64String);
      return true;
    }
    catch
    {
      return false;
    }
  }
}

internal sealed record RenderedCardImage(
  int CatalogId,
  byte[] PngBytes
);

internal sealed record RenderedCardBatch(
  IReadOnlyList<RenderedCardImage> Images,
  int MissingCount
);

internal sealed record RenderAttemptResult(
  IReadOnlyList<RenderedCardImage> Images,
  IReadOnlyDictionary<int, string> Failures
);
