/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;


namespace CardExporter.MTGO.Files;

internal static class AssetSourceFiles
{
  private static readonly string[] FileNames =
  [
    "Card.dll",
    "DuelScene.dll"
  ];

  public static IReadOnlyList<SourceFile> Enumerate(string appDirectory)
  {
    var files = new List<SourceFile>();
    foreach (string fileName in FileNames)
    {
      string path = Path.Combine(appDirectory, fileName);
      if (!File.Exists(path))
      {
        throw new FileNotFoundException($"MTGO asset source file was not found: {path}", path);
      }

      var fileInfo = new FileInfo(path);
      files.Add(new SourceFile(
        RelativePath: fileName,
        ByteCount: fileInfo.Length,
        ModifiedAtUtc: fileInfo.LastWriteTimeUtc,
        Sha256: ComputeSha256(path)
      ));
    }

    return files;
  }

  private static string ComputeSha256(string path)
  {
    using FileStream stream = File.OpenRead(path);
    return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
  }
}
