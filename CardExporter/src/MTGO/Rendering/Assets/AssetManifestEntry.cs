/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Security.Cryptography;

using CardExporter.MTGO.Rendering;

namespace CardExporter.MTGO.Rendering.Assets;

internal sealed record AssetManifestEntry(
  string Slug,
  string FileName,
  string ContentType,
  string SourceKey,
  string NormalizedSymbol,
  string ConversionMethod,
  int ByteCount,
  string Sha256
)
{
  public static AssetManifestEntry FromAsset(
    SvgAsset asset,
    string fileName,
    string sourceKey,
    string normalizedSymbol
  ) => new AssetManifestEntry(
    asset.Slug,
    fileName,
    asset.ContentType,
    sourceKey,
    normalizedSymbol,
    asset.ConversionMethod,
    asset.Bytes.Length,
    Convert.ToHexString(SHA256.HashData(asset.Bytes)).ToLowerInvariant()
  );
}
