using System.Collections.Generic;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Fru.P2DiamondDust;
using AnoMech.Scenarios.Fru.P2LightRampant;
using AnoMech.Scenarios.Fru.P3Apocalypse;
using AnoMech.Scenarios.Fru.P3UltimateRelativity;
using AnoMech.Scenarios.Fru.P4CrystallizeTime;
using AnoMech.Scenarios.Fru.P4DarklitDragonsong;
using AnoMech.Scenarios.Fru.P5FulgentBlade;
using AnoMech.Scenarios.Fru.P5ParadiseRegained;
using AnoMech.Scenarios.Top.P2PartySynergy;
using AnoMech.Scenarios.Top.P5Delta;
using AnoMech.Scenarios.Top.P5Omega;
using AnoMech.Scenarios.Top.P5Sigma;
using AnoMech.Scenarios.Top.P6WaveCannon2;
using AnoMech.Scenarios.Umad;
using AnoMech.Scenarios.Umad.P2Forsaken;
using AnoMech.Scenarios.Umad.P3BlackHole;
using AnoMech.Scenarios.Umad.P3LimitCut;
using AnoMech.Scenarios.Umad.P4KefkaSays;
using AnoMech.Scenarios.Umad.P5Celestriad;
using AnoMech.Scenarios.Umad.P5Exaflares;
using AnoMech.Scenarios.Umad.P5Flood;
using AnoMech.Scenarios.Uwu;
using AnoMech.Scenarios.Uwu.UltimatePredation;
using AnoMech.Scenarios.Uwu.UltimateSuppression;

namespace AnoMech.Integrations.Iinact;

// A line a run writes before its own, standing for a cast, hit or marker of the fight the run
// starts after. SourceBNpcBase names the spawned boss whose id the line carries; SourceName is the
// name it shows, blank where a callout on the same id would check it.
internal readonly record struct CactbotSeed(CactbotSeed.Kind Type, uint Id, uint SourceBNpcBase = 0, string SourceName = "")
{
    public enum Kind { StartsUsing, Ability, HeadMarker }
}

// What cactbot's trigger file for a fight needs to have seen to know where a scenario starts: each
// file sets its phase, and some boss ids, from the cast or hit that opens it, which a scenario
// starting mid-fight never plays. Each id below is one only the file's tracker reads without a
// source check.
internal static class CactbotSeeds
{
    // cactbot's TOP triggers offset every later head marker by the pull's first, 0x0017.
    private static readonly CactbotSeed TopFirstHeadMarker = new(CactbotSeed.Kind.HeadMarker, 0x0017);

    private static readonly CactbotSeed KefkaJump = Ability(0xC3FD, sourceName: "Kefka");

    public static IReadOnlyList<CactbotSeed> For(IScenario scenario) => scenario switch
    {
        UmadP2ForsakenScenario => [Cast(0xC24C)],
        UmadP3LimitCutScenario or UmadP3BlackHoleScenario or UmadP3StompAMoleScenario =>
            [Cast(0xC3F7), Cast(0xBAE5, UmadConstants.BNpcBaseId.KefkaP3, "Kefka")],
        UmadP4KefkaSaysScenario => [Cast(0xC2DC)],
        UmadP5FloodScenario or UmadP5CelestriadScenario or UmadP5ExaflaresScenario or UmadP5ForsakenNull =>
            [KefkaJump, KefkaJump, KefkaJump, KefkaJump, KefkaJump],

        FruP2DiamondDustScenario => [Cast(0x9CFF)],
        FruP2LightRampantScenario => [Cast(0x9D14)],
        FruP3UltimateRelativityScenario => [Cast(0x9D49)],
        FruP3ApocalypseScenario => [Cast(0x9D4D)],
        FruP4DarklitDragonsongScenario => [Cast(0x9D36)],
        FruP4CrystallizeTimeScenario => [Cast(0x9D6A)],
        FruP5FulgentBladeScenario or FruP5ParadiseRegainedScenario => [Cast(0x9D72)],

        TopP2PartySynergyScenario => [Cast(0x7B40), TopFirstHeadMarker],
        TopP5DeltaScenario => [Ability(0x7B7C), TopFirstHeadMarker],
        TopP5SigmaScenario => [Cast(0x8014), TopFirstHeadMarker],
        TopP5OmegaScenario => [Cast(0x8015), TopFirstHeadMarker],
        TopP6WaveCannon2Scenario => [Ability(0x7F72), TopFirstHeadMarker],

        UltimatePredationScenario =>
        [
            Ability(0x2B53, UwuConstants.BNpcBaseId.Garuda),
            Ability(0x2B5F, UwuConstants.BNpcBaseId.Ifrit),
            Ability(0x2CFD, UwuConstants.BNpcBaseId.Titan),
            Ability(0x2CF5),
            Ability(0x2B87, UwuConstants.BNpcBaseId.UltimaWeapon),
        ],
        UltimateSuppressionScenario => [Ability(0x2D4D)],

        _ => [],
    };

    private static CactbotSeed Cast(uint actionId, uint sourceBNpcBase = 0, string sourceName = "")
        => new(CactbotSeed.Kind.StartsUsing, actionId, sourceBNpcBase, sourceName);

    private static CactbotSeed Ability(uint actionId, uint sourceBNpcBase = 0, string sourceName = "")
        => new(CactbotSeed.Kind.Ability, actionId, sourceBNpcBase, sourceName);
}
