/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;


namespace CardExporter.CLI.Inspection;

internal static class DataDirectoryInspector
{
  public static void Inspect(string dataDirectory, ILogger logger)
  {
    logger.LogInformation("MTGO data directory: {DataDirectory}", dataDirectory);
    if (!Directory.Exists(dataDirectory))
    {
      logger.LogError("MTGO data directory does not exist: {DataDirectory}", dataDirectory);
      Environment.ExitCode = 3;
      return;
    }

    logger.LogInformation("Direct child directories:");
    foreach (string directory in Directory.EnumerateDirectories(dataDirectory).OrderBy(static d => d))
    {
      var info = new DirectoryInfo(directory);
      logger.LogInformation("  {Name}  modified={LastWriteUtc:o}", info.Name, info.LastWriteTimeUtc);
    }

    List<string> candidates = FindCandidateDirectories(dataDirectory).ToList();
    if (candidates.Count == 0)
    {
      logger.LogWarning("No CardDataSet or ValidationRuleSet candidate directories were found under {DataDirectory}.", dataDirectory);
      return;
    }

    logger.LogInformation("Candidate data directories:");
    foreach (string candidate in candidates)
    {
      DirectorySummary summary = SummarizeDirectory(candidate);
      logger.LogInformation(
        "  {Path}: files={FileCount}, directories={DirectoryCount}, bytes={ByteCount}, modified={LastWriteUtc:o}",
        candidate,
        summary.FileCount,
        summary.DirectoryCount,
        summary.ByteCount,
        summary.LastWriteUtc
      );

      if (summary.Extensions.Count > 0)
      {
        logger.LogInformation("    extensions: {Extensions}", string.Join(", ", summary.Extensions.Select(static e => $"{e.Key}:{e.Value}")));
      }

      foreach (string sample in summary.Samples)
      {
        logger.LogInformation("    sample: {Sample}", sample);
      }

      foreach (string sampleDirectory in summary.DirectorySamples)
      {
        logger.LogInformation("    directory: {SampleDirectory}", sampleDirectory);
      }
    }
  }

  private static IEnumerable<string> FindCandidateDirectories(string dataDirectory)
  {
    var pending = new Stack<(string Path, int Depth)>();
    pending.Push((dataDirectory, 0));

    while (pending.Count > 0)
    {
      (string path, int depth) = pending.Pop();
      string name = Path.GetFileName(path);
      if (IsCandidateDirectoryName(name))
      {
        yield return path;
      }

      if (depth >= 8)
      {
        continue;
      }

      IEnumerable<string> children;
      try
      {
        children = Directory.EnumerateDirectories(path);
      }
      catch (UnauthorizedAccessException)
      {
        continue;
      }
      catch (DirectoryNotFoundException)
      {
        continue;
      }

      foreach (string child in children)
      {
        pending.Push((child, depth + 1));
      }
    }
  }

  private static bool IsCandidateDirectoryName(string name) =>
    name.Contains("CardDataSet", StringComparison.OrdinalIgnoreCase) ||
    name.Contains("CardDataSource", StringComparison.OrdinalIgnoreCase) ||
    name.Contains("ValidationRuleSet", StringComparison.OrdinalIgnoreCase) ||
    name.Contains("ValidationRules", StringComparison.OrdinalIgnoreCase);

  private static DirectorySummary SummarizeDirectory(string directory)
  {
    long directoryCount = 0;
    long fileCount = 0;
    long byteCount = 0;
    DateTime lastWriteUtc = Directory.GetLastWriteTimeUtc(directory);
    Dictionary<string, long> extensions = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
    List<string> samples = new List<string>(12);
    List<string> directorySamples = new List<string>(12);

    foreach (string childDirectory in Directory.EnumerateDirectories(directory, "*", SearchOption.AllDirectories))
    {
      directoryCount++;
      var info = new DirectoryInfo(childDirectory);
      if (info.LastWriteTimeUtc > lastWriteUtc)
      {
        lastWriteUtc = info.LastWriteTimeUtc;
      }

      if (directorySamples.Count < 12)
      {
        directorySamples.Add(Path.GetRelativePath(directory, childDirectory));
      }
    }

    foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
    {
      var info = new FileInfo(file);
      fileCount++;
      byteCount += info.Length;
      if (info.LastWriteTimeUtc > lastWriteUtc)
      {
        lastWriteUtc = info.LastWriteTimeUtc;
      }

      string extension = string.IsNullOrEmpty(info.Extension) ? "<none>" : info.Extension;
      extensions.TryGetValue(extension, out long count);
      extensions[extension] = count + 1;

      if (samples.Count < 12)
      {
        samples.Add(Path.GetRelativePath(directory, file));
      }
    }

    return new DirectorySummary(fileCount, directoryCount, byteCount, lastWriteUtc, extensions, samples, directorySamples);
  }

  private sealed record DirectorySummary(
    long FileCount,
    long DirectoryCount,
    long ByteCount,
    DateTime LastWriteUtc,
    IReadOnlyDictionary<string, long> Extensions,
    IReadOnlyList<string> Samples,
    IReadOnlyList<string> DirectorySamples
  );
}
