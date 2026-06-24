/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using CardExporter.CLI;
using Microsoft.Extensions.Logging;


namespace CardExporter.CLI.Inspection;

internal static class InspectionCommand
{
  public static void Run(
    string dataDirectory,
    CommandLineOptions options,
    ILoggerFactory loggerFactory,
    ILogger logger
  )
  {
    DataDirectoryInspector.Inspect(dataDirectory, logger);
    foreach (string relativePath in options.DumpFiles)
    {
      FileInspector.Dump(dataDirectory, relativePath, options.DumpLines, logger);
    }

    foreach (string filePattern in options.ListFilePatterns)
    {
      FileInspector.List(dataDirectory, filePattern, options.ListFileLimit, logger);
    }

    foreach (int catalogId in options.FindCatalogIds)
    {
      SearchCommands.FindCatalogId(dataDirectory, catalogId, logger);
    }

    foreach (string text in options.FindTexts)
    {
      SearchCommands.FindText(dataDirectory, text, options.FindTextLimit, logger);
    }

    foreach (string lookupId in options.FindLookupIds)
    {
      SearchCommands.FindLookupId(dataDirectory, lookupId, logger);
    }

    foreach (int catalogId in options.ParseCatalogIds)
    {
      DebugCommands.ParseCatalogId(dataDirectory, catalogId, loggerFactory, logger);
    }

    foreach (LookupProbe lookupProbe in options.LookupProbes)
    {
      DebugCommands.ProbeLookup(dataDirectory, lookupProbe, loggerFactory, logger);
    }
  }
}
