/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;


namespace CardExporter.MTGO.Rendering.Mana;

internal static partial class ManaSymbolResourceDiscovery
{
  public static IReadOnlyList<ManaSymbolResource> Discover(string xaml)
  {
    Dictionary<string, ManaSymbolResource> resources = new Dictionary<string, ManaSymbolResource>(
      StringComparer.OrdinalIgnoreCase
    );
    bool hasColorlessManaTemplate = false;

    foreach (Match match in ResourceKeyAttribute().Matches(xaml))
    {
      string elementName = match.Groups["element"].Value;
      string mtgoKey = DecodeXmlAttribute(match.Groups["key"].Value);
      if (IsColorlessManaTemplate(elementName, mtgoKey))
      {
        hasColorlessManaTemplate = true;
      }

      if (!IsSymbolResourceElement(elementName) ||
          !ManaSymbolNameNormalizer.TryNormalize(mtgoKey, out string normalizedSymbol, out string slug))
      {
        continue;
      }

      resources.TryAdd(
        slug,
        new ManaSymbolResource(
          MTGOKey: mtgoKey,
          NormalizedSymbol: normalizedSymbol,
          Slug: slug,
          ElementName: elementName
        )
      );
    }

    if (hasColorlessManaTemplate)
    {
      AddGenericManaSymbols(resources);
    }

    return resources.Values
      .OrderBy(resource => resource.Slug, StringComparer.OrdinalIgnoreCase)
      .ToList();
  }

  private static bool IsColorlessManaTemplate(string elementName, string mtgoKey) =>
    string.Equals(elementName, "DataTemplate", StringComparison.OrdinalIgnoreCase) &&
    string.Equals(mtgoKey, "empty", StringComparison.Ordinal);

  private static void AddGenericManaSymbols(Dictionary<string, ManaSymbolResource> resources)
  {
    for (int value = 0; value <= 20; value++)
    {
      AddSyntheticResource(resources, value.ToString(System.Globalization.CultureInfo.InvariantCulture), "empty", "ColorlessManaSymbolTemplate");
    }

    AddSyntheticResource(resources, "X", "empty", "ColorlessManaSymbolTemplate");
  }

  private static void AddSyntheticResource(
    Dictionary<string, ManaSymbolResource> resources,
    string token,
    string mtgoKey,
    string elementName
  )
  {
    string normalizedSymbol = "{" + token + "}";
    string slug = ManaSymbolNameNormalizer.ToSlug(normalizedSymbol);
    resources.TryAdd(
      slug,
      new ManaSymbolResource(
        MTGOKey: mtgoKey,
        NormalizedSymbol: normalizedSymbol,
        Slug: slug,
        ElementName: elementName
      )
    );
  }

  private static bool IsSymbolResourceElement(string elementName)
  {
    string localName = elementName.Contains(':', StringComparison.Ordinal)
      ? elementName[(elementName.LastIndexOf(':') + 1)..]
      : elementName;

    return localName.Contains("Drawing", StringComparison.OrdinalIgnoreCase) ||
      localName.Contains("Geometry", StringComparison.OrdinalIgnoreCase) ||
      localName.Contains("Canvas", StringComparison.OrdinalIgnoreCase) ||
      localName.Contains("Viewbox", StringComparison.OrdinalIgnoreCase) ||
      string.Equals(localName, "DataTemplate", StringComparison.OrdinalIgnoreCase);
  }

  private static string DecodeXmlAttribute(string value) =>
    value
      .Replace("&quot;", "\"", StringComparison.Ordinal)
      .Replace("&apos;", "'", StringComparison.Ordinal)
      .Replace("&lt;", "<", StringComparison.Ordinal)
      .Replace("&gt;", ">", StringComparison.Ordinal)
      .Replace("&amp;", "&", StringComparison.Ordinal);

  [GeneratedRegex("<(?<element>[A-Za-z_][A-Za-z0-9_.:-]*)\\b[^>]*\\bx:Key=\"(?<key>[^\"]+)\"", RegexOptions.CultureInvariant)]
  private static partial Regex ResourceKeyAttribute();
}

internal sealed record ManaSymbolResource(
  string MTGOKey,
  string NormalizedSymbol,
  string Slug,
  string ElementName
);
