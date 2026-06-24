/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using CardExporter.CLI;
using CardExporter.MTGO;
using CardExporter.MTGO.Parsing;
using Microsoft.Extensions.Logging;


namespace CardExporter.CLI.Inspection;

internal static class DebugCommands
{
  public static void ParseCatalogId(
    string dataDirectory,
    int catalogId,
    ILoggerFactory loggerFactory,
    ILogger logger
  )
  {
    var parser = new Parser(
      dataDirectory,
      loggerFactory.CreateLogger<Parser>()
    );

    ParserDebugInfo? debugInfo = parser.DebugCatalogId(catalogId);
    if (debugInfo is null)
    {
      logger.LogWarning("Parser did not emit catalog ID {CatalogId}.", catalogId);
      return;
    }

    logger.LogInformation("Source file: {SourceFile}", debugInfo.SourceFile);
    logger.LogInformation(
      "Raw fields: catalog={CatalogId}, cardNameId={CardNameId}, cardNameTokenId={CardNameTokenId}, cardSetId={CardSetId}, collector={CollectorNumber}, objectType={ObjectType}, cloneId={CloneId}",
      debugInfo.Fields.CatalogId,
      debugInfo.Fields.CardNameId,
      debugInfo.Fields.CardNameTokenId,
      debugInfo.Fields.CardSetId,
      debugInfo.Fields.CollectorNumber,
      debugInfo.Fields.ObjectType,
      debugInfo.Fields.CloneId
    );
    logger.LogInformation(
      "Resolved lookups: cardName={CardName}, cardNameToken={CardNameToken}, setCode={SetCode}",
      debugInfo.CardName,
      debugInfo.CardNameToken,
      debugInfo.SetCode
    );

    if (debugInfo.CardRecord is not null)
    {
      logger.LogInformation(
        "Parsed card: id={Id}, set={SetCode}, collector={CollectorNumber}, name={Name}, mana={ManaCost}, type={TypeLine}, oracle={OracleText}, power={Power}, toughness={Toughness}, loyalty={Loyalty}, defense={Defense}, rarity={Rarity}, token={IsToken}",
        debugInfo.CardRecord.Id,
        debugInfo.CardRecord.SetCode,
        debugInfo.CardRecord.CollectorNumber,
        debugInfo.CardRecord.Name,
        debugInfo.CardRecord.ManaCost,
        debugInfo.CardRecord.TypeLine,
        debugInfo.CardRecord.OracleText,
        debugInfo.CardRecord.Power,
        debugInfo.CardRecord.Toughness,
        debugInfo.CardRecord.Loyalty,
        debugInfo.CardRecord.Defense,
        debugInfo.CardRecord.Rarity,
        debugInfo.CardRecord.IsToken
      );
    }
    else
    {
      logger.LogInformation("Parsed card: <filtered>");
    }
  }

  public static void ProbeLookup(
    string dataDirectory,
    LookupProbe lookupProbe,
    ILoggerFactory loggerFactory,
    ILogger logger
  )
  {
    var parser = new Parser(
      dataDirectory,
      loggerFactory.CreateLogger<Parser>()
    );
    string? value = parser.DebugLookupValue(lookupProbe.FileName, lookupProbe.LookupId);
    logger.LogInformation(
      "Lookup probe {FileName}:{LookupId} => {Value}",
      lookupProbe.FileName,
      lookupProbe.LookupId,
      value
    );
  }

}
