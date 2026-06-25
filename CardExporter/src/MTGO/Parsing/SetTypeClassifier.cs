/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;


namespace CardExporter.MTGO.Parsing;

internal static class SetTypeClassifier
{
  private const string Ancillary = "Ancillary";
  private const string CoreSet = "CoreSet";
  private const string FixedAncillary = "FixedAncillary";
  private const string LargeExpansionSet = "LargeExpansionSet";
  private const string MtgoOnly = "MTGO_ONLY";
  private const string Placeholder = "Placeholder";
  private const string PromotionalSet = "PromotionalSet";
  private const string SmallExpansionSet = "SmallExpansionSet";
  private const string TestSet = "TestSet";

  private static readonly IReadOnlyDictionary<string, string> SetTypeOverrides =
    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
      ["M10"] = CoreSet,
      ["M11"] = CoreSet,
      ["M12"] = CoreSet,
      ["M13"] = CoreSet,
      ["M14"] = CoreSet,
      ["M15"] = CoreSet,
      ["ORI"] = CoreSet,
      ["M19"] = CoreSet,
      ["M20"] = CoreSet,
      ["M21"] = CoreSet,
      ["FDN"] = CoreSet,

      ["LE"] = SmallExpansionSet,
      ["FE"] = SmallExpansionSet,
      ["IA"] = LargeExpansionSet,
      ["HM"] = SmallExpansionSet,
      ["MI"] = LargeExpansionSet,
      ["VI"] = SmallExpansionSet,
      ["WL"] = SmallExpansionSet,
      ["TE"] = LargeExpansionSet,
      ["ST"] = SmallExpansionSet,
      ["EX"] = SmallExpansionSet,
      ["UZ"] = LargeExpansionSet,
      ["UL"] = SmallExpansionSet,
      ["UD"] = SmallExpansionSet,
      ["MM"] = LargeExpansionSet,
      ["NE"] = SmallExpansionSet,
      ["PR"] = SmallExpansionSet,
      ["IN"] = LargeExpansionSet,
      ["PS"] = SmallExpansionSet,
      ["OD"] = LargeExpansionSet,
      ["TOR"] = SmallExpansionSet,
      ["JUD"] = SmallExpansionSet,
      ["ONS"] = LargeExpansionSet,
      ["LGN"] = SmallExpansionSet,
      ["SCG"] = SmallExpansionSet,
      ["MRD"] = LargeExpansionSet,
      ["SOK"] = SmallExpansionSet,
      ["RAV"] = LargeExpansionSet,
      ["GPT"] = SmallExpansionSet,
      ["TSP"] = LargeExpansionSet,
      ["PLC"] = SmallExpansionSet,
      ["FUT"] = SmallExpansionSet,
      ["LRW"] = LargeExpansionSet,
      ["MOR"] = SmallExpansionSet,
      ["SHM"] = LargeExpansionSet,
      ["EVE"] = SmallExpansionSet,
      ["ZEN"] = LargeExpansionSet,
      ["WWK"] = SmallExpansionSet,
      ["ROE"] = LargeExpansionSet,
      ["SOM"] = LargeExpansionSet,
      ["MBS"] = SmallExpansionSet,
      ["NPH"] = SmallExpansionSet,
      ["ISD"] = LargeExpansionSet,
      ["RTR"] = LargeExpansionSet,
      ["GTC"] = LargeExpansionSet,
      ["THS"] = LargeExpansionSet,
      ["JOU"] = SmallExpansionSet,
      ["KTK"] = LargeExpansionSet,
      ["FRF"] = SmallExpansionSet,
      ["OGW"] = SmallExpansionSet,
      ["SOI"] = LargeExpansionSet,
      ["KLD"] = LargeExpansionSet,
      ["HOU"] = SmallExpansionSet,
      ["XLN"] = LargeExpansionSet,
      ["RIX"] = SmallExpansionSet,
      ["GRN"] = LargeExpansionSet,
      ["RNA"] = LargeExpansionSet,
      ["WAR"] = LargeExpansionSet,
      ["ELD"] = LargeExpansionSet,
      ["THB"] = LargeExpansionSet,
      ["IKO"] = LargeExpansionSet,
      ["ZNR"] = LargeExpansionSet,
      ["KHM"] = LargeExpansionSet,
      ["STX"] = LargeExpansionSet,
      ["MID"] = LargeExpansionSet,
      ["VOW"] = LargeExpansionSet,
      ["NEO"] = LargeExpansionSet,
      ["SNC"] = LargeExpansionSet,
      ["ONE"] = LargeExpansionSet,
      ["MOM"] = LargeExpansionSet,
      ["MAT"] = SmallExpansionSet,
      ["WOE"] = LargeExpansionSet,
      ["LCI"] = LargeExpansionSet,
      ["MKM"] = LargeExpansionSet,
      ["OTJ"] = LargeExpansionSet,
      ["BLB"] = LargeExpansionSet,
      ["DSK"] = LargeExpansionSet,
      ["DFT"] = LargeExpansionSet,
      ["TDM"] = LargeExpansionSet,
      ["FIN"] = LargeExpansionSet,
      ["EOE"] = LargeExpansionSet,
      ["OM1"] = LargeExpansionSet,
      ["SPM"] = LargeExpansionSet,
      ["TLA"] = LargeExpansionSet,
      ["ECL"] = LargeExpansionSet,
      ["TMT"] = LargeExpansionSet,
      ["SOS"] = LargeExpansionSet,

      ["MSH"] = LargeExpansionSet,

      ["2XM"] = Ancillary,
      ["2X2"] = Ancillary,
      ["40K"] = FixedAncillary,
      ["ACR"] = Ancillary,
      ["AFC"] = Ancillary,
      ["ALL"] = Ancillary,
      ["A25"] = Ancillary,
      ["BBD"] = Ancillary,
      ["BIG"] = Ancillary,
      ["BLC"] = Ancillary,
      ["BOT"] = Ancillary,
      ["BR"] = Ancillary,
      ["BRC"] = Ancillary,
      ["BD"] = Ancillary,
      ["BRR"] = Ancillary,
      ["CC1"] = FixedAncillary,
      ["CH"] = Ancillary,
      ["CLU"] = Ancillary,
      ["CLB"] = Ancillary,
      ["CMM"] = Ancillary,
      ["CMR"] = Ancillary,
      ["CN2"] = Ancillary,
      ["CNS"] = Ancillary,
      ["DBL"] = Ancillary,
      ["DMC"] = Ancillary,
      ["DMR"] = Ancillary,
      ["DRC"] = Ancillary,
      ["DSC"] = Ancillary,
      ["EMA"] = Ancillary,
      ["EOC"] = Ancillary,
      ["EOS"] = Ancillary,
      ["EXP"] = Ancillary,
      ["FCA"] = Ancillary,
      ["FIC"] = Ancillary,
      ["GNT"] = FixedAncillary,
      ["GN2"] = FixedAncillary,
      ["GS1"] = FixedAncillary,
      ["IMA"] = Ancillary,
      ["INR"] = Ancillary,
      ["JMP"] = Ancillary,
      ["J22"] = Ancillary,
      ["J25"] = Ancillary,
      ["KHC"] = Ancillary,
      ["LCC"] = Ancillary,
      ["LTC"] = Ancillary,
      ["LTR"] = Ancillary,
      ["MAR"] = Ancillary,
      ["M3C"] = Ancillary,
      ["MB2"] = Ancillary,
      ["MH1"] = Ancillary,
      ["MH2"] = Ancillary,
      ["MH3"] = Ancillary,
      ["MIC"] = Ancillary,
      ["MKC"] = Ancillary,
      ["MMA"] = Ancillary,
      ["MM2"] = Ancillary,
      ["MM3"] = Ancillary,
      ["MOC"] = Ancillary,
      ["MSC"] = Ancillary,
      ["MS2"] = Ancillary,
      ["MS3"] = Ancillary,
      ["MS4"] = Ancillary,
      ["MUL"] = Ancillary,
      ["NCC"] = Ancillary,
      ["NEC"] = Ancillary,
      ["ONC"] = Ancillary,
      ["OMB"] = Ancillary,
      ["OTC"] = Ancillary,
      ["OTP"] = Ancillary,
      ["P2"] = Ancillary,
      ["P3"] = Ancillary,
      ["P4"] = Ancillary,
      ["PIP"] = Ancillary,
      ["PK"] = Ancillary,
      ["PO"] = Ancillary,
      ["PZA"] = Ancillary,
      ["REX"] = Ancillary,
      ["RVR"] = Ancillary,
      ["SCD"] = FixedAncillary,
      ["SLD"] = PromotionalSet,
      ["SL2"] = PromotionalSet,
      ["SLX"] = PromotionalSet,
      ["SOA"] = Ancillary,
      ["SOC"] = Ancillary,
      ["SPE"] = Ancillary,
      ["SPG"] = PromotionalSet,
      ["STA"] = Ancillary,
      ["TDC"] = Ancillary,
      ["TLE"] = Ancillary,
      ["TMC"] = Ancillary,
      ["TSB"] = Ancillary,
      ["TSR"] = Ancillary,
      ["UMA"] = Ancillary,
      ["UND"] = Ancillary,
      ["UNF"] = Ancillary,
      ["UNH"] = Ancillary,
      ["UST"] = Ancillary,
      ["VOC"] = Ancillary,
      ["W17"] = Ancillary,
      ["WOC"] = Ancillary,
      ["WOT"] = Ancillary,
      ["ZNC"] = Ancillary,
      ["ZNE"] = Ancillary,

      ["G18"] = FixedAncillary,
      ["H17"] = PromotionalSet,
      ["PRM_25"] = PromotionalSet,
      ["PRM_RTO"] = PromotionalSet,

      ["C02"] = Placeholder,
      ["CC"] = TestSet,
      ["DPA"] = FixedAncillary,
      ["FK"] = MtgoOnly,
      ["ICE"] = MtgoOnly,
      ["PZ1"] = MtgoOnly,
      ["PZ2"] = MtgoOnly,
      ["TD0"] = MtgoOnly,
      ["TD2"] = MtgoOnly,
      ["TOK"] = MtgoOnly,
      ["VAN"] = MtgoOnly,
      ["VMA"] = MtgoOnly
    };

  private static readonly IReadOnlyDictionary<string, string> SourceSetTypeAliases =
    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
      ["DUEL_DECKS"] = FixedAncillary,
      ["FROM THE VAULT"] = FixedAncillary,
      ["FROM_THE_VAULT"] = FixedAncillary,
      ["MTGO_ONLY"] = MtgoOnly,
      ["PAPER_SPECIAL"] = FixedAncillary,
      ["PREMIUM_DECK_SERIES"] = FixedAncillary
    };

  public static string? Resolve(string code, string? runtimeSetType, string? sourceSetType)
  {
    if (SetTypeOverrides.TryGetValue(code, out string? overrideSetType))
    {
      return overrideSetType;
    }

    string? normalizedRuntimeSetType = NormalizeSetType(runtimeSetType);
    if (normalizedRuntimeSetType is not null)
    {
      return normalizedRuntimeSetType;
    }

    string? normalizedSourceSetType = NormalizeSetType(sourceSetType);
    if (string.Equals(normalizedSourceSetType, "PAPER", StringComparison.OrdinalIgnoreCase))
    {
      return null;
    }

    return normalizedSourceSetType;
  }

  private static string? NullIfWhiteSpace(string? value)
  {
    return string.IsNullOrWhiteSpace(value) ? null : value;
  }

  private static string? NormalizeSetType(string? value)
  {
    string? setType = NullIfWhiteSpace(value);
    if (setType is null)
    {
      return null;
    }

    return SourceSetTypeAliases.TryGetValue(setType, out string? alias)
      ? alias
      : setType;
  }
}
