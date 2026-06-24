/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Drawing;
using System.Drawing.Imaging;
using System.IO;


namespace CardExporter.MTGO.Rendering.Assets;

internal static class RasterImageConverter
{
  public static byte[] ToPng(byte[] imageBytes)
  {
    using MemoryStream input = new MemoryStream(imageBytes, writable: false);
    using Image image = Image.FromStream(
      input,
      useEmbeddedColorManagement: false,
      validateImageData: true
    );
    using MemoryStream output = new MemoryStream();
    image.Save(output, ImageFormat.Png);
    return output.ToArray();
  }
}
