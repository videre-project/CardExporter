/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Text.RegularExpressions;


namespace CardExporter.Database.R2;

internal static partial class CardImageKey
{
  private const string CardPathPrefix = "cards/";
  private const string ProductPathPrefix = "products/";

  public static string Create(int catalogId)
  {
    return Create(catalogId, CardImageKind.Card);
  }

  public static string Create(int catalogId, CardImageKind kind)
  {
    string prefix = kind == CardImageKind.Product ? ProductPathPrefix : CardPathPrefix;
    return prefix + CreateLegacyRoot(catalogId);
  }

  public static string CreateLegacyRoot(int catalogId)
  {
    return $"{catalogId}-300px.png";
  }

  public static string PublicUrl(string publicBaseUrl, int catalogId)
  {
    return PublicUrl(publicBaseUrl, catalogId, CardImageKind.Card);
  }

  public static string PublicUrl(string publicBaseUrl, int catalogId, CardImageKind kind)
  {
    return $"{publicBaseUrl.TrimEnd('/')}/{Create(catalogId, kind)}";
  }

  public static string PublicUrl(string publicBaseUrl, string key)
  {
    return $"{publicBaseUrl.TrimEnd('/')}/{key}";
  }

  public static bool IsLegacyRootKey(string key)
  {
    return !key.Contains('/', StringComparison.Ordinal) &&
      TryParseCatalogId(key, out _);
  }

  public static bool TryParseCatalogId(string keyOrPath, out int catalogId)
  {
    string fileName = GetFileNameWithoutExtension(keyOrPath);
    Match match = CatalogImageName().Match(fileName);
    if (!match.Success)
    {
      catalogId = 0;
      return false;
    }

    return int.TryParse(match.Groups["catalogId"].Value, out catalogId);
  }

  public static bool TryParseImageKey(
    string key,
    out int catalogId,
    out CardImageKind kind
  )
  {
    if (key.StartsWith(CardPathPrefix, StringComparison.Ordinal))
    {
      kind = CardImageKind.Card;
      return TryParseCatalogId(key, out catalogId);
    }

    if (key.StartsWith(ProductPathPrefix, StringComparison.Ordinal))
    {
      kind = CardImageKind.Product;
      return TryParseCatalogId(key, out catalogId);
    }

    if (IsLegacyRootKey(key))
    {
      kind = CardImageKind.Card;
      return TryParseCatalogId(key, out catalogId);
    }

    catalogId = 0;
    kind = CardImageKind.Card;
    return false;
  }

  private static string GetFileNameWithoutExtension(string keyOrPath)
  {
    int separatorIndex = keyOrPath.LastIndexOfAny(['/', '\\']);
    string fileName = separatorIndex >= 0 ? keyOrPath[(separatorIndex + 1)..] : keyOrPath;
    int suffixIndex = fileName.IndexOfAny(['?', '#']);
    if (suffixIndex >= 0)
    {
      fileName = fileName[..suffixIndex];
    }

    int extensionIndex = fileName.LastIndexOf('.');
    return extensionIndex > 0 ? fileName[..extensionIndex] : fileName;
  }

  [GeneratedRegex("^(?<catalogId>\\d+)(?:-300px)?$", RegexOptions.CultureInvariant)]
  private static partial Regex CatalogImageName();
}
