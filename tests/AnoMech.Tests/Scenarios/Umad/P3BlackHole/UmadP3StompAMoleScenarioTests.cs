using System.Numerics;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Scenarios.Umad.P3BlackHole;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Tests.NegativeRun;
using ActionId = AnoMech.Scenarios.Umad.UmadConstants.ActionId;
using BNpcBaseId = AnoMech.Scenarios.Umad.UmadConstants.BNpcBaseId;
using StatusId = AnoMech.Scenarios.Umad.UmadConstants.StatusId;
using TimelineId = AnoMech.Scenarios.Umad.UmadConstants.TimelineId;

namespace AnoMech.Tests;

// Black Hole's pinned rolls (UmadP3BlackHoleScenarioTests.Pin), started at Stomp-a-Mole. Times are
// written on Black Hole's timeline, as in its tests, and moved up to this run's clock.
public class UmadP3StompAMoleScenarioTests
{
    private static float OnRunClock(float blackHoleTime) => blackHoleTime - UmadP3StompAMoleScenario.StartsAtSeconds;

    private static NegativeRun<UmadP3StompAMoleScenario> StompAMole(PartyRole player)
        => Negative<UmadP3StompAMoleScenario>(player)
            .Overrides<UmadP3BlackHoleStateOverrides>(UmadP3BlackHoleScenarioTests.Pin);

    [Test]
    public void OpensOnTheStompsWithNoBlackHole()
    {
        var (run, timeline) = RunPinned();

        Assert.That(run.Passed, run.ToString);
        var casts = timeline.Rows.Where(row => row["kind"] == "cast").ToList();
        Assert.Multiple(() =>
        {
            Assert.That(timeline.Rows.Where(row => row["kind"] == "spawn" && row["id"] == BNpcBaseId.BlackHole.ToString()), Is.Empty);
            Assert.That(casts.Select(row => row["id"]), Does.Not.Contain(ActionId.Earthquake.ToString()));
            Assert.That(FirstCastAt(casts, ActionId.BlizzardIII_Cast), Is.EqualTo(OnRunClock(143.61f)).Within(0.1f));
            Assert.That(FirstCastAt(casts, ActionId.StompAMole_Cast), Is.EqualTo(OnRunClock(145.52f)).Within(0.1f));
        });
    }

    // What the skipped part would have left: the bosses' buffs and chase, Kefka placed and shown.
    [Test]
    public void PicksTheBossesUpWhereBlackHoleLeftThem()
    {
        var (_, timeline) = RunPinned();
        var chaos = ActorOf(timeline, BNpcBaseId.ChaosP3);
        var exdeath = ActorOf(timeline, BNpcBaseId.Exdeath);
        var kefka = ActorOf(timeline, BNpcBaseId.KefkaP3);

        Assert.Multiple(() =>
        {
            Assert.That(RowsOf(timeline, "status_gain", chaos, StatusId.EpicVillain.ToString(), before: 0.5f), Is.Not.Empty);
            Assert.That(RowsOf(timeline, "status_gain", exdeath, StatusId.FatedVillain.ToString(), before: 0.5f), Is.Not.Empty);
            Assert.That(RowsOf(timeline, "status_gain", kefka, StatusId.Max.ToString(), before: 0.5f), Is.Not.Empty);
            Assert.That(RowsOf(timeline, "move", chaos, before: 4f), Is.Not.Empty);
            Assert.That(RowsOf(timeline, "move", exdeath, before: 4f), Is.Not.Empty);
            Assert.That(RowsOf(timeline, "set_pos", kefka, before: 0.5f), Is.Not.Empty);
            Assert.That(RowsOf(timeline, "ac", kefka, "0x197", before: 1f).Select(row => row["p"]),
                Does.Contain($"0x{TimelineId.KefkaP3Show:X}"));
        });
    }

    // The last wave's holes never spawn in this run, though its damage window is still open.
    [Test]
    public void StandingOnASkippedWavesHoleIsSafe()
    {
        var damageDown = true;
        var run = ScenarioRun.Execute(typeof(UmadP3StompAMoleScenario), 0, 1, new ScenarioRunOptions
        {
            PlayerRole = MeleeDpsA,
            Overrides = o => UmadP3BlackHoleScenarioTests.Pin((UmadP3BlackHoleStateOverrides)o),
            Takeovers = [new Takeover(0.3f, new Vector2(17f, 0f))],
            StopAt = 2.5f,
            Probe = p =>
            {
                if (p.Crossed(OnRunClock(139.5f))) damageDown = p.World.Party.Player!.HasStatus(StatusId.DamageDown);
            },
        });

        Assert.That(run.Failure, Is.Null);
        Assert.That(damageDown, Is.False);
    }

    [Test]
    public void StayingPutThroughBlizzardDies()
        => StompAMole(MeleeDpsA)
            .FreezeAt(OnRunClock(146.9f))
            .ShouldKill(ActionId.BlizzardIII, MeleeDpsA);

    [Test]
    public void SoloTowerSoakDies()
        => StompAMole(MeleeDpsA)
            .MoveBotAt(OnRunClock(151.7f), PhysRangedDps, to: new(0, 0))
            .ShouldKill(ActionId.StompAMole, MeleeDpsA);

    [Test]
    public void KnockDownShortABodyKillsStackers()
        => StompAMole(MainTank)
            .TeleportAt(OnRunClock(152f), to: new(0, -8))
            .ShouldKill(ActionId.KnockDown, OffTank, RegenHealer, ShieldHealer);

    private static (ScenarioRun Run, SimTimeline Timeline) RunPinned()
    {
        SimTimeline? timeline = null;
        var run = ScenarioRun.Execute(typeof(UmadP3StompAMoleScenario), 0, 1, new ScenarioRunOptions
        {
            Overrides = o => UmadP3BlackHoleScenarioTests.Pin((UmadP3BlackHoleStateOverrides)o),
            Probe = _ => timeline ??= ((FakeBattleCharas)Natives.BattleCharas).Timeline,
        });
        return (run, timeline!);
    }

    private static string ActorOf(SimTimeline timeline, uint bnpcBase)
        => timeline.Rows.Single(row => row["kind"] == "spawn" && row["id"] == bnpcBase.ToString())["actor"];

    private static IEnumerable<Dictionary<string, string>> RowsOf(SimTimeline timeline, string kind, string actor,
        string? id = null, float before = float.MaxValue)
        => timeline.Rows.Where(row => row["kind"] == kind && row["actor"] == actor && (id is null || row["id"] == id)
                                      && float.Parse(row["t"], System.Globalization.CultureInfo.InvariantCulture) < before);

    private static float FirstCastAt(IEnumerable<Dictionary<string, string>> casts, uint actionId)
        => float.Parse(casts.First(row => row["id"] == actionId.ToString())["t"], System.Globalization.CultureInfo.InvariantCulture);
}
