/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.IO;
using System.Reflection.PortableExecutable;
using System.Resources;
using ICSharpCode.BamlDecompiler;
using ICSharpCode.Decompiler.Metadata;


namespace CardExporter.MTGO.Rendering;

internal sealed class BamlResourceExtractor
{
  public string ExtractXaml(string assemblyPath, string bamlResourceName)
  {
    using PEFile module = new PEFile(
      assemblyPath,
      PEStreamOptions.PrefetchEntireImage
    );

    UniversalAssemblyResolver resolver = new UniversalAssemblyResolver(
      assemblyPath,
      throwOnError: false,
      module.Metadata.DetectTargetFrameworkId()
    );
    resolver.AddSearchDirectory(Path.GetDirectoryName(assemblyPath)!);

    BamlDecompilerTypeSystem typeSystem = new BamlDecompilerTypeSystem(module, resolver);
    XamlDecompiler decompiler = new XamlDecompiler(typeSystem, new BamlDecompilerSettings());

    foreach (var resource in module.Resources)
    {
      if (resource.ResourceType != ResourceType.Embedded ||
          !resource.Name.EndsWith(".g.resources", StringComparison.OrdinalIgnoreCase))
      {
        continue;
      }

      using Stream? resourceStream = resource.TryOpenStream();
      if (resourceStream is null)
      {
        continue;
      }

      string? xaml = TryExtractFromResourceSet(resourceStream, bamlResourceName, decompiler);
      if (!string.IsNullOrWhiteSpace(xaml))
      {
        return xaml;
      }
    }

    throw new InvalidOperationException($"{bamlResourceName} was not found in {assemblyPath}.");
  }

  private static string? TryExtractFromResourceSet(
    Stream resourceStream,
    string bamlResourceName,
    XamlDecompiler decompiler
  )
  {
    using ResourceReader resourceReader = new ResourceReader(resourceStream);
    var enumerator = resourceReader.GetEnumerator();
    while (enumerator.MoveNext())
    {
      if (enumerator.Key is not string entryName ||
          !string.Equals(entryName, bamlResourceName, StringComparison.OrdinalIgnoreCase))
      {
        continue;
      }

      resourceReader.GetResourceData(entryName, out _, out byte[] data);
      using MemoryStream bamlStream = new MemoryStream(data, 4, data.Length - 4);
      var result = decompiler.Decompile(bamlStream);
      return result.Xaml.ToString();
    }

    return null;
  }
}
