/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Text.Json;
using CardExporter.MTGO.Parsing;


namespace CardExporter.MTGO.Records;

internal static class CardCatalogVariantTypes
{
  public const string FoilClone = "foil_clone";
}

internal sealed record CardCatalogVariantRecord(
  int CatalogId,
  int CardId,
  string VariantType,
  string? SetCode,
  string? Name,
  int? CardTextureNumber,
  bool? IsFoil,
  bool? IsToken,
  string RawJson
)
{
  public static CardCatalogVariantRecord? Create(
    DigitalObjectFields fields,
    CardRecord baseCard,
    LookupTables lookups
  )
  {
    if (fields.CatalogId is not int catalogId ||
        fields.CloneId != baseCard.Id ||
        !fields.IsFoilClone)
    {
      return null;
    }

    return new CardCatalogVariantRecord(
      CatalogId: catalogId,
      CardId: baseCard.Id,
      VariantType: CardCatalogVariantTypes.FoilClone,
      SetCode: lookups.ResolveSetCode(fields) ?? baseCard.SetCode,
      Name: lookups.ResolveCardName(fields) ?? baseCard.Name,
      CardTextureNumber: fields.CardTextureNumber,
      IsFoil: fields.IsFoil,
      IsToken: fields.IsToken ?? baseCard.IsToken,
      RawJson: JsonSerializer.Serialize(fields)
    );
  }
}
