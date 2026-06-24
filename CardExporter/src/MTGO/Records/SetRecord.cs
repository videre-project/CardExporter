/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Text.Json;
using CardExporter.MTGO.Parsing;


namespace CardExporter.MTGO.Records;

internal sealed record SetRecord(
  string Code,
  string? Name,
  DateOnly? ReleaseDate,
  int? Age,
  string? SetType,
  string SourceFile,
  string RawJson
)
{
  public static SetRecord Create(
    string code,
    string? age,
    string? cardsetType,
    string sourceFile,
    SetMetadata? metadata
  )
  {
    var raw = new
    {
      age,
      cardsetType
    };

    return new SetRecord(
      Code: code,
      Name: metadata?.Name,
      ReleaseDate: metadata?.ReleaseDate,
      Age: metadata?.Age ?? (TryParseInt(age, out int parsedAge) ? parsedAge : null),
      SetType: metadata?.SetType ?? NullIfWhiteSpace(cardsetType),
      SourceFile: sourceFile,
      RawJson: JsonSerializer.Serialize(raw)
    );
  }

  private static string? NullIfWhiteSpace(string? value)
  {
    return string.IsNullOrWhiteSpace(value) ? null : value;
  }

  private static bool TryParseInt(string? value, out int result)
  {
    result = 0;
    return !string.IsNullOrWhiteSpace(value) && int.TryParse(value, out result);
  }
}
