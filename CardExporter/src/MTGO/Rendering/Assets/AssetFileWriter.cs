/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.IO;

using CardExporter.MTGO.Rendering;

namespace CardExporter.MTGO.Rendering.Assets;

internal static class AssetFileWriter
{
  public static void ResetDirectory(string path)
  {
    if (Directory.Exists(path))
    {
      Directory.Delete(path, recursive: true);
    }

    Directory.CreateDirectory(path);
  }

  public static string Write(string outputRoot, SvgAsset asset)
  {
    string fileName = asset.Slug + "." + asset.Extension;
    File.WriteAllBytes(Path.Combine(outputRoot, fileName), asset.Bytes);
    return fileName;
  }
}
