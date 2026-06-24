/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;


namespace CardExporter.CLI.Inspection;

internal static class FileInspector
{
  public static void Dump(string dataDirectory, string relativePath, int maxLines, ILogger logger)
  {
    string path = Path.Combine(dataDirectory, relativePath);
    logger.LogInformation("Dumping first {MaxLines} lines from {RelativePath}", maxLines, relativePath);

    if (!File.Exists(path))
    {
      logger.LogError("Requested dump file does not exist: {Path}", path);
      Environment.ExitCode = 4;
      return;
    }

    using var reader = new StreamReader(path);
    for (int lineNumber = 1; lineNumber <= maxLines && reader.ReadLine() is string line; lineNumber++)
    {
      logger.LogInformation("{LineNumber,4}: {Line}", lineNumber, line);
    }
  }

  public static void List(string dataDirectory, string relativePattern, int limit, ILogger logger)
  {
    string normalizedPattern = relativePattern.Replace('\\', '/');
    string? relativeDirectory = Path.GetDirectoryName(normalizedPattern);
    string searchPattern = Path.GetFileName(normalizedPattern);
    string directory = string.IsNullOrWhiteSpace(relativeDirectory)
      ? dataDirectory
      : Path.Combine(dataDirectory, relativeDirectory);

    logger.LogInformation("Listing up to {Limit} files matching {RelativePattern}", limit, relativePattern);
    if (!Directory.Exists(directory))
    {
      logger.LogError("List directory does not exist: {Directory}", directory);
      Environment.ExitCode = 6;
      return;
    }

    int count = 0;
    foreach (string file in Directory.EnumerateFiles(directory, searchPattern).OrderBy(static f => f))
    {
      count++;
      if (count <= limit)
      {
        var info = new FileInfo(file);
        logger.LogInformation("  {File}  bytes={ByteCount}", Path.GetRelativePath(dataDirectory, file), info.Length);
      }
    }

    logger.LogInformation("Matched {FileCount} files for {RelativePattern}.", count, relativePattern);
  }
}
