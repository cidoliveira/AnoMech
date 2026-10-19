using AnoMech.Integrations.Iinact;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Top;
using AnoMech.Scenarios.Ucob;
using AnoMech.Scenarios.Umad;
using AnoMech.Scenarios.Umad.P1TeleTrouncing;
using AnoMech.Scenarios.Umad.P3BlackHole;
using AnoMech.Scenarios.Umad.P5Flood;
using AnoMech.Scenarios.Uwu.UltimatePredation;
using static AnoMech.Integrations.Iinact.CactbotSeed;
using UwuBNpcBaseId = AnoMech.Scenarios.Uwu.UwuConstants.BNpcBaseId;

namespace AnoMech.Tests;

public class CactbotSeedsTests
{
    private static IEnumerable<TestCaseData> CatalogScenarios()
        => from scenario in ScenarioCatalog.Create().Scenarios
           select new TestCaseData(scenario).SetName(scenario.FullName());

    // Every phase but the first opens on a cast or hit cactbot keys its phase on, and UCOB's
    // triggers don't track phases at all.
    [TestCaseSource(nameof(CatalogScenarios))]
    public void EveryScenarioPastAFightsFirstPhaseTellsCactbotWhereItIs(IScenario scenario)
    {
        var expectsNone = scenario is UmadP1TeleTrouncingScenario || scenario.Phase.Zone is UcobZone;
        Assert.That(CactbotSeeds.For(scenario), expectsNone ? Is.Empty : Is.Not.Empty);
    }

    [Test]
    public void TopRunsSeedThePhaseThenTheFightsFirstHeadMarker()
    {
        foreach (var scenario in ScenarioCatalog.Create().Scenarios.Where(s => s.Phase.Zone is TopZone))
        {
            var seeds = CactbotSeeds.For(scenario);
            Assert.That(seeds[^1], Is.EqualTo(new CactbotSeed(Kind.HeadMarker, 0x0017)), scenario.FullName());
            Assert.That(seeds[0].Type, Is.Not.EqualTo(Kind.HeadMarker), scenario.FullName());
        }
    }

    [Test]
    public void DancingMadP3NamesKefkaForTheTriggersThatFollowHim()
        => Assert.That(CactbotSeeds.For(new UmadP3StompAMoleScenario()), Is.EqualTo(new[]
        {
            new CactbotSeed(Kind.StartsUsing, 0xC3F7),
            new CactbotSeed(Kind.StartsUsing, 0xBAE5, UmadConstants.BNpcBaseId.KefkaP3, "Kefka"),
        }));

    // Its tracker counts Kefka's jumps to the middle; the fifth starts P5.
    [Test]
    public void DancingMadP5ReplaysKefkasFiveJumps()
        => Assert.That(CactbotSeeds.For(new UmadP5FloodScenario()),
            Has.Count.EqualTo(5).And.All.EqualTo(new CactbotSeed(Kind.Ability, 0xC3FD, SourceName: "Kefka")));

    // Its tracker only moves on from each primal in turn, and keeps each one's id.
    [Test]
    public void PredationWalksUwusPhasesInOrderWithEachPrimal()
        => Assert.That(CactbotSeeds.For(new UltimatePredationScenario()).Select(s => (s.Id, s.SourceBNpcBase)), Is.EqualTo(new[]
        {
            (0x2B53u, UwuBNpcBaseId.Garuda),
            (0x2B5Fu, UwuBNpcBaseId.Ifrit),
            (0x2CFDu, UwuBNpcBaseId.Titan),
            (0x2CF5u, 0u),
            (0x2B87u, UwuBNpcBaseId.UltimaWeapon),
        }));
}
