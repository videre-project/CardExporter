/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Text.Json;
using CardExporter.MTGO.Parsing;


namespace CardExporter.MTGO.Records;

internal sealed record ProductRecord(
  int Id,
  string? SetCode,
  string? Name,
  string? ObjectType,
  int? TextureNumber,
  bool? IsTradable,
  string RawJson
)
{
  public static ProductRecord? Create(
    DigitalObjectFields fields,
    LookupTables lookups
  )
  {
    if (fields.CatalogId is not int catalogId ||
        !fields.IsProductObject)
    {
      return null;
    }

    return new ProductRecord(
      Id: catalogId,
      SetCode: lookups.ResolveSetCode(fields),
      Name: lookups.ResolveCardName(fields),
      ObjectType: fields.ObjectType,
      TextureNumber: fields.CardTextureNumber,
      IsTradable: fields.IsTradable,
      RawJson: JsonSerializer.Serialize(fields)
    );
  }
}
