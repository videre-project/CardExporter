/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;


namespace CardExporter.MTGO.Files;

internal sealed class CardDataFileIndex
{
  private readonly string _dataDirectory;
  private readonly ILogger _logger;
  private string[]? _setFiles;
  private string[]? _productFiles;
  private string[]? _sourceDirectories;

  public CardDataFileIndex(string dataDirectory, ILogger logger)
  {
    _dataDirectory = dataDirectory;
    _logger = logger;
    SourceDirectory = Path.Combine(dataDirectory, "CardDataSource");
  }

  public string SourceDirectory { get; }

  public IEnumerable<SourceFile> EnumerateParsedSourceFiles()
  {
    HashSet<string> seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (string file in GetSetFiles())
    {
      if (seenPaths.Add(file))
      {
        yield return CreateSourceFile(file);
      }
    }

    foreach (string file in GetProductFiles())
    {
      if (seenPaths.Add(file))
      {
        yield return CreateSourceFile(file);
      }
    }

    foreach (string file in EnumerateSharedCardDataSourceFiles())
    {
      if (seenPaths.Add(file))
      {
        yield return CreateSourceFile(file);
      }
    }
  }

  public IEnumerable<SourceFile> EnumerateSharedSourceFiles()
  {
    HashSet<string> seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (string file in EnumerateSharedCardDataSourceFiles())
    {
      if (seenPaths.Add(file))
      {
        yield return CreateSourceFile(file);
      }
    }
  }

  public IEnumerable<SourceFile> EnumerateLegalitySourceFiles()
  {
    HashSet<string> seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (string file in EnumerateValidationRuleSetFiles())
    {
      if (seenPaths.Add(file))
      {
        yield return CreateSourceFile(file);
      }
    }
  }

  public IReadOnlyList<string> GetSetFiles()
  {
    if (!Directory.Exists(SourceDirectory))
    {
      throw new DirectoryNotFoundException($"CardDataSource directory was not found under {_dataDirectory}.");
    }

    if (_setFiles is null)
    {
      _setFiles = Directory
        .EnumerateFiles(SourceDirectory, "client_*.xml")
        .Where(static f => !f.EndsWith("_DO.xml", StringComparison.OrdinalIgnoreCase))
        .Order(StringComparer.OrdinalIgnoreCase)
        .ToArray();
      _logger.LogInformation("Found {SetFileCount} card set files.", _setFiles.Length);
    }

    return _setFiles;
  }

  public IReadOnlyList<string> GetProductFiles()
  {
    if (!Directory.Exists(SourceDirectory))
    {
      throw new DirectoryNotFoundException($"CardDataSource directory was not found under {_dataDirectory}.");
    }

    if (_productFiles is null)
    {
      _productFiles = Directory
        .EnumerateFiles(SourceDirectory, "client_*_DO.xml")
        .Order(StringComparer.OrdinalIgnoreCase)
        .ToArray();
      _logger.LogInformation("Found {ProductFileCount} MTGO product object files.", _productFiles.Length);
    }

    return _productFiles;
  }

  public IEnumerable<string> EnumerateValidationRuleSetFiles()
  {
    foreach (string directory in GetSourceDirectories())
    {
      string name = Path.GetFileName(directory);
      if (!name.Contains("ValidationRule", StringComparison.OrdinalIgnoreCase) &&
          !name.Contains("ValidationRules", StringComparison.OrdinalIgnoreCase))
      {
        continue;
      }

      foreach (string file in Directory.EnumerateFiles(directory, "*.xml", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase))
      {
        yield return file;
      }
    }
  }

  private IEnumerable<string> EnumerateSharedCardDataSourceFiles()
  {
    if (!Directory.Exists(SourceDirectory))
    {
      yield break;
    }

    foreach (string file in Directory.EnumerateFiles(SourceDirectory, "*.xml", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase))
    {
      string fileName = Path.GetFileName(file);
      if (IsSetFileName(fileName) || IsDigitalObjectFileName(fileName))
      {
        continue;
      }

      yield return file;
    }
  }

  public string GetRelativePath(string path)
  {
    return Path.GetRelativePath(_dataDirectory, path)
      .Replace('\\', '/')
      .Replace(Path.DirectorySeparatorChar, '/');
  }

  private IReadOnlyList<string> GetSourceDirectories()
  {
    if (_sourceDirectories is null)
    {
      string[] candidateNames =
      [
        "CardDataSource",
        "CardDataSet",
        "ValidationRulesets",
        "ValidationRuleSets",
        "ValidationRuleSet"
      ];

      _sourceDirectories = candidateNames
        .Select(name => Path.Combine(_dataDirectory, name))
        .Where(Directory.Exists)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Order(StringComparer.OrdinalIgnoreCase)
        .ToArray();
    }

    return _sourceDirectories;
  }

  private static string ComputeSha256(string path)
  {
    using FileStream stream = File.OpenRead(path);
    return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
  }

  private SourceFile CreateSourceFile(string path)
  {
    var fileInfo = new FileInfo(path);
    return new SourceFile(
      RelativePath: GetRelativePath(path),
      ByteCount: fileInfo.Length,
      ModifiedAtUtc: fileInfo.LastWriteTimeUtc,
      Sha256: ComputeSha256(path)
    );
  }

  private static bool IsSetFileName(string fileName)
  {
    return fileName.StartsWith("client_", StringComparison.OrdinalIgnoreCase) &&
           fileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
           !IsDigitalObjectFileName(fileName);
  }

  private static bool IsDigitalObjectFileName(string fileName)
  {
    return fileName.EndsWith("_DO.xml", StringComparison.OrdinalIgnoreCase);
  }
}
