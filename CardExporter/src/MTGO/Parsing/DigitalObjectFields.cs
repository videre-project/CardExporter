/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;


namespace CardExporter.MTGO.Parsing;

internal sealed record DigitalObjectFields(
  int? CatalogId,
  string? CurrentSetCode,
  string? CardNameId,
  string? CardNameTokenId,
  string? CardSetId,
  string? CollectorNumber,
  string? ObjectType,
  int? ArtId,
  string? ArtistId,
  int? CloneId,
  int? CardTextureNumber,
  int? OtherFaceTextureNumber,
  int? HiddenSubcardChildTextureNumber,
  int? HiddenSubcardParentTextureNumber,
  IReadOnlyList<int> SplitCardCatalogIds,
  int? SplitParentCatalogId,
  int? SplitOtherCardCatalogId,
  string? ColorId,
  string? ColorIdentityId,
  string? ConvertedManaCostId,
  string? FlavorTextId,
  string? ManaCostId,
  string? OracleTextId,
  string? PromoLabelId,
  string? RarityId,
  string? Power,
  string? Toughness,
  string? PowerToughnessId,
  string? LoyaltyId,
  string? DefenseId,
  int? FrameStyle,
  bool? HasActivatedAbility,
  bool? ShouldWork,
  bool? IsAdventure,
  bool? IsFoil,
  bool? IsToken,
  bool? IsContainer,
  bool? IsDigitalObject,
  bool? IsPrecon,
  bool? IsTradable,
  IReadOnlyList<string> Supertypes,
  IReadOnlyList<string> CardTypes,
  IReadOnlyList<string> SubtypeIds
)
{
  public bool IsCardObject =>
    ObjectType is null ||
    string.Equals(ObjectType, "CARD", StringComparison.OrdinalIgnoreCase) ||
    string.Equals(ObjectType, "TOKN", StringComparison.OrdinalIgnoreCase);

  public bool IsSubcard =>
    string.Equals(ObjectType, "SUBC", StringComparison.OrdinalIgnoreCase);

  public bool IsNonCardObject =>
    !IsCardObject;

  public bool IsProductObject =>
    IsNonCardObject && !IsSubcard;

  public int? CollectorNumberValue => ParseCollectorNumberValue(CollectorNumber);

  public void ApplyPowerToughness(LookupTables lookups, ref string? power, ref string? toughness)
  {
    string? powerToughness = lookups.GetPowerToughness(PowerToughnessId);
    if (string.IsNullOrWhiteSpace(powerToughness))
    {
      return;
    }

    int slashIndex = powerToughness.IndexOf('/');
    if (slashIndex <= 0 || slashIndex >= powerToughness.Length - 1)
    {
      return;
    }

    power ??= powerToughness[..slashIndex];
    toughness ??= powerToughness[(slashIndex + 1)..];
  }

  private static int? ParseCollectorNumberValue(string? collectorNumber)
  {
    if (string.IsNullOrWhiteSpace(collectorNumber))
    {
      return null;
    }

    ReadOnlySpan<char> span = collectorNumber.AsSpan().Trim();
    int length = 0;
    while (length < span.Length && char.IsDigit(span[length]))
    {
      length++;
    }

    return length > 0 && int.TryParse(span[..length], out int value) ? value : null;
  }
}
