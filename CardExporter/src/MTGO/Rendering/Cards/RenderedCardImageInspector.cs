/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;


namespace CardExporter.MTGO.Rendering.Cards;

internal static class RenderedCardImageInspector
{
  private const int MaxSupportedDimension = 4096;

  private static readonly byte[] PngSignature =
  [
    0x89, 0x50, 0x4E, 0x47,
    0x0D, 0x0A, 0x1A, 0x0A
  ];

  private static readonly RelativeRegion[] CandidateArtRegions =
  [
    new("normal art box", 0.09, 0.91, 0.13, 0.53),
    new("wide upper art area", 0.08, 0.92, 0.13, 0.43),
    new("tall upper art area", 0.09, 0.91, 0.13, 0.62),
    new("left-side art panel", 0.08, 0.56, 0.12, 0.70),
    new("right-side art panel", 0.44, 0.92, 0.12, 0.70)
  ];

  public static bool HasLikelyMissingArt(byte[] pngBytes, out string diagnostic)
  {
    diagnostic = string.Empty;
    if (!TryDecodePngSafely(pngBytes, out DecodedPngImage image))
    {
      return false;
    }

    foreach (RelativeRegion region in CandidateArtRegions)
    {
      ArtRegionMeasurement measurement = MeasureRegion(image, region);
      if (!IsLikelyMissingArtPlaceholder(measurement))
      {
        continue;
      }

      diagnostic = string.Format(
        CultureInfo.InvariantCulture,
        "{0}: art mean {1:F1}, stddev {2:F2}, unique colors {3}",
        region.Name,
        measurement.Mean,
        measurement.StandardDeviation,
        measurement.UniqueColorCount
      );
      return true;
    }

    return false;
  }

  public static bool IsSupportedPng(byte[] pngBytes)
  {
    return TryDecodePngSafely(pngBytes, out _);
  }

  public static bool TryReadDimensions(byte[] pngBytes, out PngDimensions dimensions)
  {
    dimensions = default;
    try
    {
      return TryReadPngDimensions(pngBytes, out dimensions);
    }
    catch (Exception exception) when (
      exception is InvalidDataException ||
      exception is IOException ||
      exception is ArgumentException ||
      exception is NotSupportedException ||
      exception is OverflowException)
    {
      dimensions = default;
      return false;
    }
  }

  private static bool TryDecodePngSafely(byte[] pngBytes, out DecodedPngImage image)
  {
    try
    {
      return TryDecodePng(pngBytes, out image);
    }
    catch (Exception exception) when (
      exception is InvalidDataException ||
      exception is IOException ||
      exception is ArgumentException ||
      exception is NotSupportedException ||
      exception is OverflowException)
    {
      image = default;
      return false;
    }
  }

  private static bool IsLikelyMissingArtPlaceholder(ArtRegionMeasurement measurement)
  {
    return measurement.Mean is >= 35.0 and <= 90.0 &&
      measurement.StandardDeviation < 12.0 &&
      measurement.UniqueColorCount < 150;
  }

  private static bool TryDecodePng(byte[] pngBytes, out DecodedPngImage image)
  {
    image = default;
    if (pngBytes.Length < PngSignature.Length || !pngBytes.AsSpan(0, PngSignature.Length).SequenceEqual(PngSignature))
    {
      return false;
    }

    int width = 0;
    int height = 0;
    int bitDepth = 0;
    int colorType = 0;
    int interlaceMethod = 0;
    using var compressedData = new MemoryStream();

    int offset = PngSignature.Length;
    while (offset + 8 <= pngBytes.Length)
    {
      int chunkLength = BinaryPrimitives.ReadInt32BigEndian(pngBytes.AsSpan(offset, 4));
      offset += 4;
      if (chunkLength < 0 || offset + 4 + chunkLength + 4 > pngBytes.Length)
      {
        return false;
      }

      ReadOnlySpan<byte> chunkType = pngBytes.AsSpan(offset, 4);
      offset += 4;
      ReadOnlySpan<byte> chunkData = pngBytes.AsSpan(offset, chunkLength);
      offset += chunkLength + 4;

      if (chunkType.SequenceEqual("IHDR"u8))
      {
        if (chunkData.Length < 13)
        {
          return false;
        }

        width = BinaryPrimitives.ReadInt32BigEndian(chunkData[..4]);
        height = BinaryPrimitives.ReadInt32BigEndian(chunkData.Slice(4, 4));
        bitDepth = chunkData[8];
        colorType = chunkData[9];
        interlaceMethod = chunkData[12];
      }
      else if (chunkType.SequenceEqual("IDAT"u8))
      {
        compressedData.Write(chunkData);
      }
      else if (chunkType.SequenceEqual("IEND"u8))
      {
        break;
      }
    }

    if (width <= 0 ||
        height <= 0 ||
        width > MaxSupportedDimension ||
        height > MaxSupportedDimension ||
        bitDepth != 8 ||
        interlaceMethod != 0)
    {
      return false;
    }

    int bytesPerPixel = colorType switch
    {
      2 => 3,
      6 => 4,
      _ => 0
    };
    if (bytesPerPixel == 0)
    {
      return false;
    }

    byte[] decompressedBytes;
    compressedData.Position = 0;
    using (var zlibStream = new ZLibStream(compressedData, CompressionMode.Decompress))
    using (var decompressedData = new MemoryStream())
    {
      zlibStream.CopyTo(decompressedData);
      decompressedBytes = decompressedData.ToArray();
    }

    int stride = checked(width * bytesPerPixel);
    int expectedLength = checked(height * (stride + 1));
    if (decompressedBytes.Length < expectedLength)
    {
      return false;
    }

    byte[] previousRow = new byte[stride];
    byte[] currentRow = new byte[stride];
    byte[] rgbPixels = new byte[width * height * 3];
    int scanlineOffset = 0;

    for (int y = 0; y < height; y++)
    {
      byte filterType = decompressedBytes[scanlineOffset++];
      Array.Copy(decompressedBytes, scanlineOffset, currentRow, 0, stride);
      scanlineOffset += stride;
      UnfilterScanline(currentRow, previousRow, bytesPerPixel, filterType);

      for (int x = 0; x < width; x++)
      {
        int sourceOffset = x * bytesPerPixel;
        int targetOffset = ((y * width) + x) * 3;
        rgbPixels[targetOffset] = currentRow[sourceOffset];
        rgbPixels[targetOffset + 1] = currentRow[sourceOffset + 1];
        rgbPixels[targetOffset + 2] = currentRow[sourceOffset + 2];
      }

      (previousRow, currentRow) = (currentRow, previousRow);
    }

    image = new DecodedPngImage(width, height, rgbPixels);
    return true;
  }

  private static bool TryReadPngDimensions(byte[] pngBytes, out PngDimensions dimensions)
  {
    dimensions = default;
    if (pngBytes.Length < PngSignature.Length || !pngBytes.AsSpan(0, PngSignature.Length).SequenceEqual(PngSignature))
    {
      return false;
    }

    int offset = PngSignature.Length;
    while (offset + 8 <= pngBytes.Length)
    {
      int chunkLength = BinaryPrimitives.ReadInt32BigEndian(pngBytes.AsSpan(offset, 4));
      offset += 4;
      if (chunkLength < 0 || offset + 4 + chunkLength + 4 > pngBytes.Length)
      {
        return false;
      }

      ReadOnlySpan<byte> chunkType = pngBytes.AsSpan(offset, 4);
      offset += 4;
      ReadOnlySpan<byte> chunkData = pngBytes.AsSpan(offset, chunkLength);
      offset += chunkLength + 4;

      if (!chunkType.SequenceEqual("IHDR"u8))
      {
        continue;
      }

      if (chunkData.Length < 8)
      {
        return false;
      }

      int width = BinaryPrimitives.ReadInt32BigEndian(chunkData[..4]);
      int height = BinaryPrimitives.ReadInt32BigEndian(chunkData.Slice(4, 4));
      if (width <= 0 ||
          height <= 0 ||
          width > MaxSupportedDimension ||
          height > MaxSupportedDimension)
      {
        return false;
      }

      dimensions = new PngDimensions(width, height);
      return true;
    }

    return false;
  }

  private static ArtRegionMeasurement MeasureRegion(DecodedPngImage image, RelativeRegion region)
  {
    int artX0 = Math.Clamp((int)(image.Width * region.X0), 0, image.Width);
    int artX1 = Math.Clamp((int)(image.Width * region.X1), artX0, image.Width);
    int artY0 = Math.Clamp((int)(image.Height * region.Y0), 0, image.Height);
    int artY1 = Math.Clamp((int)(image.Height * region.Y1), artY0, image.Height);
    var uniqueColors = new HashSet<int>();
    double sum = 0.0;
    double sumSquares = 0.0;
    int pixelCount = 0;

    for (int y = artY0; y < artY1; y++)
    {
      for (int x = artX0; x < artX1; x++)
      {
        int pixelOffset = ((y * image.Width) + x) * 3;
        int red = image.RgbPixels[pixelOffset];
        int green = image.RgbPixels[pixelOffset + 1];
        int blue = image.RgbPixels[pixelOffset + 2];
        double luminance = (red + green + blue) / 3.0;
        sum += luminance;
        sumSquares += luminance * luminance;
        pixelCount++;

        if (uniqueColors.Count < 10_000)
        {
          uniqueColors.Add((red << 16) | (green << 8) | blue);
        }
      }
    }

    if (pixelCount == 0)
    {
      return default;
    }

    double mean = sum / pixelCount;
    double variance = Math.Max(0.0, (sumSquares / pixelCount) - (mean * mean));
    return new ArtRegionMeasurement(
      mean,
      Math.Sqrt(variance),
      uniqueColors.Count
    );
  }

  private static void UnfilterScanline(
    byte[] currentRow,
    byte[] previousRow,
    int bytesPerPixel,
    byte filterType
  )
  {
    for (int i = 0; i < currentRow.Length; i++)
    {
      int left = i >= bytesPerPixel ? currentRow[i - bytesPerPixel] : 0;
      int up = previousRow[i];
      int upperLeft = i >= bytesPerPixel ? previousRow[i - bytesPerPixel] : 0;
      int predictor = filterType switch
      {
        0 => 0,
        1 => left,
        2 => up,
        3 => (left + up) / 2,
        4 => Paeth(left, up, upperLeft),
        _ => throw new InvalidDataException($"Unsupported PNG filter type {filterType}.")
      };

      currentRow[i] = unchecked((byte)(currentRow[i] + predictor));
    }
  }

  private static int Paeth(int left, int up, int upperLeft)
  {
    int estimate = left + up - upperLeft;
    int leftDistance = Math.Abs(estimate - left);
    int upDistance = Math.Abs(estimate - up);
    int upperLeftDistance = Math.Abs(estimate - upperLeft);
    if (leftDistance <= upDistance && leftDistance <= upperLeftDistance)
    {
      return left;
    }

    return upDistance <= upperLeftDistance ? up : upperLeft;
  }

  private readonly record struct ArtRegionMeasurement(
    double Mean,
    double StandardDeviation,
    int UniqueColorCount
  );

  private readonly record struct DecodedPngImage(
    int Width,
    int Height,
    byte[] RgbPixels
  );

  public readonly record struct PngDimensions(
    int Width,
    int Height
  );

  private readonly record struct RelativeRegion(
    string Name,
    double X0,
    double X1,
    double Y0,
    double Y1
  );
}
