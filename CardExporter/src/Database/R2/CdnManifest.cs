/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;


namespace CardExporter.Database.R2;

internal sealed class CdnManifest
{
  private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
  private readonly Dictionary<string, CdnManifestRow> _rows;

  private CdnManifest(Dictionary<string, CdnManifestRow> rows)
  {
    _rows = rows;
  }

  public int Count => _rows.Count;

  public IReadOnlySet<int> ImageCatalogIds => _rows.Values
    .Select(static row => row.CatalogId)
    .Where(static catalogId => catalogId is not null)
    .Select(static catalogId => catalogId!.Value)
    .ToHashSet();

  public static CdnManifest Empty()
  {
    return new CdnManifest(new Dictionary<string, CdnManifestRow>(StringComparer.Ordinal));
  }

  public static CdnManifest Load(string path)
  {
    if (!File.Exists(path))
    {
      return Empty();
    }

    using var reader = new StreamReader(path);
    string? headerLine = reader.ReadLine();
    if (headerLine is null)
    {
      return Empty();
    }

    List<string> header = ParseCsvRecord(headerLine, path, lineNumber: 1);
    return HasColumn(header, "key")
      ? LoadKeyedManifest(reader, path, header)
      : LoadLegacyImageManifest(reader, path, header);
  }

  public bool TryGet(string key, out CdnManifestRow row)
  {
    return _rows.TryGetValue(key, out row!);
  }

  public bool ContainsKeyPrefix(string prefix)
  {
    string normalizedPrefix = prefix.TrimEnd('/') + "/";
    return _rows.Keys.Any(key => key.StartsWith(normalizedPrefix, StringComparison.Ordinal));
  }

  public void UpsertImage(
    int catalogId,
    string publicBaseUrl,
    DateTimeOffset modifiedAt,
    CardImageKind kind
  )
  {
    string key = CardImageKey.Create(catalogId, kind);
    Upsert(new CdnManifestRow(
      Key: key,
      Url: CardImageKey.PublicUrl(publicBaseUrl, key),
      ModifiedAt: FormatTimestamp(modifiedAt),
      ContentType: "image/png",
      ByteCount: null,
      Sha256: null,
      CatalogId: catalogId
    ));
  }

  public void UpsertAsset(
    string key,
    string publicBaseUrl,
    DateTimeOffset modifiedAt,
    string contentType,
    long byteCount,
    string sha256
  )
  {
    Upsert(new CdnManifestRow(
      Key: key,
      Url: CardImageKey.PublicUrl(publicBaseUrl, key),
      ModifiedAt: FormatTimestamp(modifiedAt),
      ContentType: contentType,
      ByteCount: byteCount,
      Sha256: sha256,
      CatalogId: null
    ));
  }

  public void UpsertObject(
    R2Object item,
    string publicBaseUrl,
    CdnManifest? previousManifest = null
  )
  {
    CdnManifestRow? existing = null;
    if (previousManifest is not null)
    {
      previousManifest.TryGet(item.Key, out existing);
    }
    else
    {
      _rows.TryGetValue(item.Key, out existing);
    }

    string? sha256 = existing?.ByteCount == item.Size ? existing?.Sha256 : null;
    Upsert(new CdnManifestRow(
      Key: item.Key,
      Url: CardImageKey.PublicUrl(publicBaseUrl, item.Key),
      ModifiedAt: FormatTimestamp(item.LastModified),
      ContentType: existing?.ContentType ?? GuessContentType(item.Key),
      ByteCount: item.Size,
      Sha256: sha256,
      CatalogId: TryGetImageCatalogId(item.Key, out int catalogId) ? catalogId : null
    ));
  }

  public bool RemoveImage(int catalogId)
  {
    bool removed = false;
    foreach (string key in _rows.Keys
      .Where(key => TryGetImageCatalogId(key, out int rowCatalogId) && rowCatalogId == catalogId)
      .ToList())
    {
      removed |= _rows.Remove(key);
    }

    return removed;
  }

  public void Write(string path)
  {
    string? directory = Path.GetDirectoryName(path);
    if (!string.IsNullOrWhiteSpace(directory))
    {
      Directory.CreateDirectory(directory);
    }

    byte[] bytes = Utf8NoBom.GetBytes(CreateCsv());
    string temporaryPath = path + ".tmp";
    File.WriteAllBytes(temporaryPath, bytes);
    File.Move(temporaryPath, path, overwrite: true);
  }

  private void Upsert(CdnManifestRow row)
  {
    _rows[row.Key] = row;
  }

  private string CreateCsv()
  {
    var builder = new StringBuilder();
    builder.Append("key,url,modified_at,content_type,byte_count,sha256\n");
    foreach (CdnManifestRow row in _rows.Values.OrderBy(static row => row.Key, CdnManifestKeyComparer.Instance))
    {
      AppendCsvField(builder, row.Key);
      builder.Append(',');
      AppendCsvField(builder, row.Url);
      builder.Append(',');
      AppendCsvField(builder, row.ModifiedAt);
      builder.Append(',');
      AppendCsvField(builder, row.ContentType ?? string.Empty);
      builder.Append(',');
      builder.Append(row.ByteCount?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
      builder.Append(',');
      AppendCsvField(builder, row.Sha256 ?? string.Empty);
      builder.Append('\n');
    }

    return builder.ToString();
  }

  private static CdnManifest LoadKeyedManifest(
    StreamReader reader,
    string path,
    IReadOnlyList<string> header
  )
  {
    int keyIndex = FindColumn(header, "key", path);
    int urlIndex = FindColumn(header, "url", path);
    int modifiedAtIndex = FindColumn(header, "modified_at", path);
    int contentTypeIndex = FindOptionalColumn(header, "content_type");
    int byteCountIndex = FindOptionalColumn(header, "byte_count");
    int sha256Index = FindOptionalColumn(header, "sha256");
    var rows = new Dictionary<string, CdnManifestRow>(StringComparer.Ordinal);

    int lineNumber = 1;
    while (!reader.EndOfStream)
    {
      lineNumber++;
      string? line = reader.ReadLine();
      if (string.IsNullOrWhiteSpace(line))
      {
        continue;
      }

      List<string> fields = ParseCsvRecord(line, path, lineNumber);
      int requiredFieldCount = Math.Max(keyIndex, Math.Max(urlIndex, modifiedAtIndex)) + 1;
      if (fields.Count < requiredFieldCount)
      {
        throw new InvalidOperationException($"CDN manifest row {lineNumber} had too few columns: {path}");
      }

      string key = fields[keyIndex];
      if (string.IsNullOrWhiteSpace(key))
      {
        throw new InvalidOperationException($"CDN manifest row {lineNumber} had an empty key: {path}");
      }

      long? byteCount = null;
      if (TryGetField(fields, byteCountIndex, out string? byteCountValue) &&
          !string.IsNullOrWhiteSpace(byteCountValue))
      {
        if (!long.TryParse(byteCountValue, NumberStyles.None, CultureInfo.InvariantCulture, out long parsedByteCount))
        {
          throw new InvalidOperationException($"CDN manifest row {lineNumber} had an invalid byte_count: {path}");
        }

        byteCount = parsedByteCount;
      }

      UpsertLatest(rows, new CdnManifestRow(
        Key: key,
        Url: fields[urlIndex],
        ModifiedAt: fields[modifiedAtIndex],
        ContentType: TryGetField(fields, contentTypeIndex, out string? contentType) ? EmptyToNull(contentType) : null,
        ByteCount: byteCount,
        Sha256: TryGetField(fields, sha256Index, out string? sha256) ? EmptyToNull(sha256) : null,
        CatalogId: TryGetImageCatalogId(key, out int catalogId) ? catalogId : null
      ));
    }

    return new CdnManifest(rows);
  }

  private static CdnManifest LoadLegacyImageManifest(
    StreamReader reader,
    string path,
    IReadOnlyList<string> header
  )
  {
    int catalogIdIndex = FindColumn(header, "catalog_id", path);
    int urlIndex = FindColumn(header, "url", path);
    int modifiedAtIndex = FindColumn(header, "modified_at", path);
    var rows = new Dictionary<string, CdnManifestRow>(StringComparer.Ordinal);

    int lineNumber = 1;
    while (!reader.EndOfStream)
    {
      lineNumber++;
      string? line = reader.ReadLine();
      if (string.IsNullOrWhiteSpace(line))
      {
        continue;
      }

      List<string> fields = ParseCsvRecord(line, path, lineNumber);
      int requiredFieldCount = Math.Max(catalogIdIndex, Math.Max(urlIndex, modifiedAtIndex)) + 1;
      if (fields.Count < requiredFieldCount)
      {
        throw new InvalidOperationException($"Image manifest row {lineNumber} had too few columns: {path}");
      }

      if (!int.TryParse(fields[catalogIdIndex], NumberStyles.None, CultureInfo.InvariantCulture, out int catalogId))
      {
        throw new InvalidOperationException($"Image manifest row {lineNumber} had an invalid catalog_id: {path}");
      }

      string url = fields[urlIndex];
      string key = TryGetUrlPathKey(url, out string parsedKey)
        ? parsedKey
        : CardImageKey.Create(catalogId);

      UpsertLatest(rows, new CdnManifestRow(
        Key: key,
        Url: url,
        ModifiedAt: fields[modifiedAtIndex],
        ContentType: "image/png",
        ByteCount: null,
        Sha256: null,
        CatalogId: catalogId
      ));
    }

    return new CdnManifest(rows);
  }

  private static bool TryGetImageCatalogId(string key, out int catalogId)
  {
    return CardImageKey.TryParseImageKey(key, out catalogId, out _);
  }

  private static bool TryGetUrlPathKey(string url, out string key)
  {
    key = string.Empty;
    if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
    {
      return false;
    }

    key = uri.AbsolutePath.TrimStart('/');
    return key.Length > 0;
  }

  private static string? GuessContentType(string key)
  {
    string extension = Path.GetExtension(key);
    if (string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase))
    {
      return "image/png";
    }

    if (string.Equals(extension, ".svg", StringComparison.OrdinalIgnoreCase))
    {
      return "image/svg+xml";
    }

    return null;
  }

  private static void UpsertLatest(
    Dictionary<string, CdnManifestRow> rows,
    CdnManifestRow row
  )
  {
    if (!rows.TryGetValue(row.Key, out CdnManifestRow? existingRow) ||
        CompareModifiedAt(row.ModifiedAt, existingRow.ModifiedAt) >= 0)
    {
      rows[row.Key] = row;
    }
  }

  private static int CompareModifiedAt(string left, string right)
  {
    bool hasLeft = DateTimeOffset.TryParse(left, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset leftTime);
    bool hasRight = DateTimeOffset.TryParse(right, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset rightTime);
    if (hasLeft && hasRight)
    {
      return leftTime.CompareTo(rightTime);
    }

    return string.Compare(left, right, StringComparison.Ordinal);
  }

  private static bool HasColumn(IReadOnlyList<string> header, string columnName) =>
    header.Any(column => string.Equals(column, columnName, StringComparison.OrdinalIgnoreCase));

  private static int FindColumn(IReadOnlyList<string> header, string columnName, string path)
  {
    int index = FindOptionalColumn(header, columnName);
    if (index >= 0)
    {
      return index;
    }

    throw new InvalidOperationException($"CDN manifest was missing required column {columnName}: {path}");
  }

  private static int FindOptionalColumn(IReadOnlyList<string> header, string columnName)
  {
    for (int i = 0; i < header.Count; i++)
    {
      if (string.Equals(header[i], columnName, StringComparison.OrdinalIgnoreCase))
      {
        return i;
      }
    }

    return -1;
  }

  public static string FormatTimestamp(DateTimeOffset value)
  {
    return value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
  }

  private static bool TryGetField(
    IReadOnlyList<string> fields,
    int index,
    out string? value
  )
  {
    if (index < 0 || index >= fields.Count)
    {
      value = null;
      return false;
    }

    value = fields[index];
    return true;
  }

  private static string? EmptyToNull(string? value) =>
    string.IsNullOrWhiteSpace(value) ? null : value;

  private static List<string> ParseCsvRecord(
    string line,
    string path,
    int lineNumber
  )
  {
    var fields = new List<string>();
    var field = new StringBuilder();
    bool inQuotes = false;

    for (int i = 0; i < line.Length; i++)
    {
      char value = line[i];
      if (inQuotes)
      {
        if (value == '"' && i + 1 < line.Length && line[i + 1] == '"')
        {
          field.Append('"');
          i++;
          continue;
        }

        if (value == '"')
        {
          inQuotes = false;
          continue;
        }

        field.Append(value);
        continue;
      }

      if (value == '"')
      {
        inQuotes = true;
        continue;
      }

      if (value == ',')
      {
        fields.Add(field.ToString());
        field.Clear();
        continue;
      }

      field.Append(value);
    }

    fields.Add(field.ToString());
    if (inQuotes)
    {
      throw new InvalidOperationException($"CDN manifest row {lineNumber} had an unterminated quoted field: {path}");
    }

    return fields;
  }

  private static void AppendCsvField(StringBuilder builder, string value)
  {
    if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\r') && !value.Contains('\n'))
    {
      builder.Append(value);
      return;
    }

    builder.Append('"');
    builder.Append(value.Replace("\"", "\"\"", StringComparison.Ordinal));
    builder.Append('"');
  }
}

internal sealed class CdnManifestKeyComparer : IComparer<string>
{
  public static readonly CdnManifestKeyComparer Instance = new();

  private CdnManifestKeyComparer()
  {
  }

  public int Compare(string? x, string? y)
  {
    if (ReferenceEquals(x, y))
    {
      return 0;
    }

    if (x is null)
    {
      return -1;
    }

    if (y is null)
    {
      return 1;
    }

    string xPrefix = GetPrefix(x);
    string yPrefix = GetPrefix(y);
    int prefixComparison = string.Compare(xPrefix, yPrefix, StringComparison.Ordinal);
    if (prefixComparison != 0)
    {
      return prefixComparison;
    }

    if (CardImageKey.TryParseImageKey(x, out int xCatalogId, out _) &&
        CardImageKey.TryParseImageKey(y, out int yCatalogId, out _))
    {
      int catalogIdComparison = xCatalogId.CompareTo(yCatalogId);
      if (catalogIdComparison != 0)
      {
        return catalogIdComparison;
      }
    }

    return string.Compare(x, y, StringComparison.Ordinal);
  }

  private static string GetPrefix(string key)
  {
    int separatorIndex = key.IndexOf('/');
    return separatorIndex < 0 ? string.Empty : key[..separatorIndex];
  }
}

internal sealed record CdnManifestRow(
  string Key,
  string Url,
  string ModifiedAt,
  string? ContentType,
  long? ByteCount,
  string? Sha256,
  int? CatalogId
);
