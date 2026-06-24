/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Security.Cryptography;

using CardExporter.MTGO.Rendering;

namespace CardExporter.MTGO.Rendering.Sets;

internal sealed record SetSymbolName(
  string SetCode,
  string Rarity,
  string Slug
);

internal sealed record SetSymbolManifestEntry(
  string SetCode,
  string Rarity,
  string Slug,
  string FileName,
  string ContentType,
  string ResourceName,
  string ConversionMethod,
  int ByteCount,
  string Sha256
)
{
  public static SetSymbolManifestEntry FromAsset(
    SetSymbolName name,
    SvgAsset asset,
    string fileName,
    string resourceName
  ) => new SetSymbolManifestEntry(
    name.SetCode,
    name.Rarity,
    name.Slug,
    fileName,
    asset.ContentType,
    resourceName,
    asset.ConversionMethod,
    asset.Bytes.Length,
    Convert.ToHexString(SHA256.HashData(asset.Bytes)).ToLowerInvariant()
  );
}
