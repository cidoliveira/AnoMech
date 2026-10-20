using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios.Umad.P4KefkaSays;
using static AnoMech.Scenarios.Umad.UmadConstants;

namespace AnoMech.Tests;

// What a scenario hangs on an enemy's cast to follow its ActionEffect, cast by a helper spawned into
// a running scenario.
public class EnemyActionCastTests
{
    private const uint NineSecondCast = ActionId.GrandCross;
    private const uint InstantAction = ActionId.AutoAttack1;

    [Test]
    public void WhatRunsAfterReleaseGoesOutRightBehindTheActionEffect()
    {
        Dictionary<string, string>? lastSent = null;
        var helper = RunWithHelper(stopAt: 12f, (caster, timeline) =>
            caster.Cast(NineSecondCast).AfterRelease(() => lastSent = timeline.Rows[^1]));

        Assert.That(lastSent, Is.Not.Null, "never released");
        Assert.Multiple(() =>
        {
            Assert.That(lastSent!["kind"], Is.EqualTo("ability"));
            Assert.That(lastSent["id"], Is.EqualTo(NineSecondCast.ToString()));
            Assert.That(lastSent["actor"], Is.EqualTo(helper));
        });
    }

    [Test]
    public void HungOnAnActionAlreadyOutItRunsAtOnce()
    {
        var ranAtOnce = false;
        RunWithHelper(stopAt: 3f, (caster, _) =>
        {
            caster.Cast(InstantAction).AfterRelease(() => ranAtOnce = true);
            Assert.That(ranAtOnce, Is.True);
        });
    }

    [Test]
    public void ACancelledCastNeverRunsIt()
    {
        var ran = false;
        RunWithHelper(stopAt: 12f, (caster, _) =>
        {
            caster.Cast(NineSecondCast).AfterRelease(() => ran = true);
            caster.CancelCast(CastCancelReason.Cancelled);
        });

        Assert.That(ran, Is.False);
    }

    // `act` runs at 2 s on a helper spawned at 1 s; returns the helper's entity id as the timeline
    // writes it.
    private static string RunWithHelper(float stopAt, Action<SimEnemy, SimTimeline> act)
    {
        SimTimeline? timeline = null;
        SimEnemy? helper = null;
        var helperId = "";
        var run = ScenarioRun.Execute(typeof(UmadP4KefkaSaysScenario), 0, 1, new ScenarioRunOptions
        {
            Overrides = o => UmadP4KefkaSaysScenarioTests.Pin((UmadP4KefkaSaysStateOverrides)o),
            StopAt = stopAt,
            Probe = p =>
            {
                timeline ??= ((FakeBattleCharas)Natives.BattleCharas).Timeline;
                if (p.Crossed(1f))
                    helper = p.World.SpawnEnemy(new EnemySpawnConfig(BNpcBaseId: BNpcBaseId.KefkaHelper, NameId: BNpcNameId.Kefka,
                        Level: 1, Targetable: false, EnemyList: EnemyListMode.Never, Visibility: SpawnVisibility.InvisibleHelper,
                        Placement: new Placement(Vector3.Zero, 0f)));
                if (p.Crossed(2f))
                {
                    helperId = helper!.EntityId.ToString("X8");
                    act(helper, timeline);
                }
            },
        });
        Assert.That(run.Failure, Is.Null, run.ToString);
        return helperId;
    }
}
