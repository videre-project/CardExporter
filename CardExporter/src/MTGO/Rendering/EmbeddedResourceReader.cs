/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Resources;


namespace CardExporter.MTGO.Rendering;

internal static class EmbeddedResourceReader
{
  public static byte[] ReadRequired(string assemblyPath, string key)
  {
    foreach (EmbeddedResourceFile resource in Enumerate(assemblyPath))
    {
      if (string.Equals(resource.Key, NormalizeKey(key), StringComparison.OrdinalIgnoreCase))
      {
        return resource.Bytes;
      }
    }

    throw new InvalidOperationException($"{key} was not found in {assemblyPath}.");
  }

  public static IEnumerable<EmbeddedResourceFile> Enumerate(string assemblyPath)
  {
    using FileStream assemblyStream = File.OpenRead(assemblyPath);
    using PEReader peReader = new PEReader(assemblyStream);
    if (!peReader.HasMetadata)
    {
      yield break;
    }

    MetadataReader metadataReader = peReader.GetMetadataReader();
    DirectoryEntry? resourcesDirectory = peReader.PEHeaders.CorHeader?.ResourcesDirectory;
    if (resourcesDirectory is null || resourcesDirectory.Value.RelativeVirtualAddress == 0)
    {
      yield break;
    }

    PEMemoryBlock resourcesBlock = peReader.GetSectionData(resourcesDirectory.Value.RelativeVirtualAddress);
    foreach (ManifestResourceHandle resourceHandle in metadataReader.ManifestResources)
    {
      ManifestResource resource = metadataReader.GetManifestResource(resourceHandle);
      if (!resource.Implementation.IsNil)
      {
        continue;
      }

      string resourceName = metadataReader.GetString(resource.Name);
      if (!resourceName.EndsWith(".resources", StringComparison.OrdinalIgnoreCase))
      {
        continue;
      }

      BlobReader reader = resourcesBlock.GetReader(
        (int)resource.Offset,
        resourcesBlock.Length - (int)resource.Offset
      );
      int resourceLength = reader.ReadInt32();
      byte[] resourceBytes = reader.ReadBytes(resourceLength);

      using MemoryStream resourceStream = new MemoryStream(resourceBytes, writable: false);
      using ResourceReader resourceReader = new ResourceReader(resourceStream);
      foreach (DictionaryEntry entry in resourceReader)
      {
        if (entry.Key is not string entryName)
        {
          continue;
        }

        byte[]? bytes = entry.Value switch
        {
          byte[] array => array,
          Stream stream => ReadAllBytes(stream),
          _ => null
        };

        if (bytes is null)
        {
          continue;
        }

        yield return new EmbeddedResourceFile(
          Path.GetFileName(assemblyPath),
          resourceName,
          NormalizeKey(entryName),
          bytes
        );
      }
    }
  }

  private static byte[] ReadAllBytes(Stream stream)
  {
    if (stream.CanSeek)
    {
      stream.Position = 0;
    }

    using MemoryStream memoryStream = new MemoryStream();
    stream.CopyTo(memoryStream);
    return memoryStream.ToArray();
  }

  private static string NormalizeKey(string key) =>
    key.Replace('\\', '/').TrimStart('/');
}

internal sealed record EmbeddedResourceFile(
  string AssemblyName,
  string ResourceStream,
  string Key,
  byte[] Bytes
);
