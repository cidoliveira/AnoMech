using System.Numerics;
using AnoMech.Integrations.Iinact;

namespace AnoMech.Tests;

// Expected lines follow the layout of real ACT logs (cactbot's docs/LogGuide.md and netlog_defs).
public class NetLogLineTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 19, 20, 0, 0, 500, TimeSpan.FromHours(-3));
    private const string Stamp = "2026-10-19T20:00:00.5000000-03:00";
    private const string Hash = "0000000000000000";

    private static readonly NetLogActor Kefka = new(0x40001234, "Kefka");
    private static readonly NetLogActor Machinist = new(0x10FF0001, "Machinist");

    // The game's height axis is ACT's z: x, then the ground plane's other axis, then height.
    private static readonly Vector3 World = new(100f, 2.5f, 90f);

    private static string Line(NetLogEntry entry) => entry.ToLine(At);

    [Test]
    public void StartsUsingPutsTheCastersPositionInActsAxisOrder()
        => Assert.That(Line(NetLogLine.StartsUsing(Kefka, 0xBAEF, "Stomp-a-Mole", Kefka, 5f, World, 1.5708f)),
            Is.EqualTo($"20|{Stamp}|40001234|Kefka|BAEF|Stomp-a-Mole|40001234|Kefka|5.00|100.000|90.000|2.500|1.571|{Hash}"));

    [Test]
    public void StartsUsingExtraCarriesTheCastsOwnLocation()
        => Assert.That(Line(NetLogLine.StartsUsingExtra(Kefka, 0xBAEF, World, -3.1416f)),
            Is.EqualTo($"263|{Stamp}|40001234|BAEF|100.000|90.000|2.500|-3.142|{Hash}"));

    [Test]
    public void HeadMarkerNamesTheMarkedActorAndTheLockon()
        => Assert.That(Line(NetLogLine.HeadMarker(Machinist, 0x157)),
            Is.EqualTo($"27|{Stamp}|10FF0001|Machinist|0000|0000|0157|0000|0000|0000|{Hash}"));

    [Test]
    public void TetherRepeatsTheTargetAfterTheTetherId()
        => Assert.That(Line(NetLogLine.Tether(Kefka, Machinist, 0xF6, 15)),
            Is.EqualTo($"35|{Stamp}|40001234|Kefka|10FF0001|Machinist|0000|0000|00F6|10FF0001|000F|0000|{Hash}"));

    [Test]
    public void AbilityHasActsFiftyFourFieldsWithTheHitsAndBothSides()
    {
        var hit = new NetLogHit(Machinist, [(0x750003, 0x4620000), (0xE, 0x13DC0000)], Hp: 36022, MaxHp: 36022, Mp: 5200, MaxMp: 10000,
            Position: new Vector3(1f, 0f, 2f), Heading: 0.5f);
        var line = Line(NetLogLine.Ability(Kefka, 0xF67, "Aetherochemical Laser", hit, targetIndex: 0, targetCount: 1,
            sourceHp: (16000, 16000, 8840, 10000), sourcePosition: World, sourceHeading: 1.5f, sequence: 0xDE1F,
            animationId: 0xF67, animationLock: 0.6f, rotation: 0x12AB));
        var fields = line.Split('|');

        Assert.Multiple(() =>
        {
            Assert.That(fields, Has.Length.EqualTo(55));
            Assert.That(fields[0], Is.EqualTo("21"));
            Assert.That(fields[2..10], Is.EqualTo(new[] { "40001234", "Kefka", "F67", "Aetherochemical Laser", "10FF0001", "Machinist", "750003", "4620000" }));
            Assert.That(fields[10..12], Is.EqualTo(new[] { "E", "13DC0000" }));
            Assert.That(fields[12..24], Is.All.EqualTo("0"));
            Assert.That(fields[24..34], Is.EqualTo(new[] { "36022", "36022", "5200", "10000", "0", "1000", "1.000", "2.000", "0.000", "0.500" }));
            Assert.That(fields[34..44], Is.EqualTo(new[] { "16000", "16000", "8840", "10000", "0", "1000", "100.000", "90.000", "2.500", "1.500" }));
            Assert.That(fields[44..54], Is.EqualTo(new[] { "0000DE1F", "0", "1", "E0000000", "", "01", "F67", "F67", "0.600", "12AB" }));
            Assert.That(fields[54], Is.EqualTo(Hash));
        });
    }

    [Test]
    public void AnAbilityOnSeveralTargetsIsTheAoeType()
    {
        var hit = new NetLogHit(Machinist, [], 0, 0, 0, 0, Vector3.Zero, 0f);
        var line = Line(NetLogLine.Ability(Kefka, 0xF67, "Laser", hit, targetIndex: 1, targetCount: 2,
            sourceHp: (0, 0, 0, 0), sourcePosition: Vector3.Zero, sourceHeading: 0f, sequence: 1, animationId: 0xF67, animationLock: 0f, rotation: 0));

        Assert.That(line, Does.StartWith("22|"));
    }

    [Test]
    public void AnAbilityThatHitNobodyNamesNoTarget()
    {
        var line = Line(NetLogLine.Ability(Kefka, 0xF67, "Laser", NetLogHit.Nobody, targetIndex: 0, targetCount: 0,
            sourceHp: (0, 0, 0, 0), sourcePosition: Vector3.Zero, sourceHeading: 0f, sequence: 1, animationId: 0xF67, animationLock: 0f, rotation: 0));

        Assert.That(line.Split('|')[6..8], Is.EqualTo(new[] { "E0000000", "" }));
    }

    [Test]
    public void AbilityExtraCarriesTheEffectsLocationAndAnimationTarget()
        => Assert.That(Line(NetLogLine.AbilityExtra(Kefka, 0xF67, 0x664, World, 3.1416f, Machinist.Id)),
            Is.EqualTo($"264|{Stamp}|40001234|0F67|00000664|1|100.000|90.000|2.500|3.142|10FF0001|{Hash}"));

    [Test]
    public void GainsEffectHasTheDurationAndStacks()
        => Assert.That(Line(NetLogLine.GainsEffect(0x13DC, "Forsaken", 24.5f, Kefka, Machinist, stacks: 2, targetMaxHp: 100000, sourceMaxHp: 500000)),
            Is.EqualTo($"26|{Stamp}|13DC|Forsaken|24.50|40001234|Kefka|10FF0001|Machinist|02|100000|500000|{Hash}"));

    [Test]
    public void LosesEffectHasNoDuration()
        => Assert.That(Line(NetLogLine.LosesEffect(0x13DC, "Forsaken", Kefka, Machinist, stacks: 0, targetMaxHp: 100000, sourceMaxHp: 500000)),
            Is.EqualTo($"30|{Stamp}|13DC|Forsaken|0.00|40001234|Kefka|10FF0001|Machinist|00|100000|500000|{Hash}"));

    [Test]
    public void ActorControlIsTheDirectorsUpdate()
        => Assert.That(Line(NetLogLine.ActorControl(0x800375D2, 0x80000027, 0x1B, 0x2, 0x1BDB, 0x40004141)),
            Is.EqualTo($"33|{Stamp}|800375D2|80000027|1B|02|1BDB|40004141|{Hash}"));

    [Test]
    public void MapEffectHasTheFlagsAndSlot()
        => Assert.That(Line(NetLogLine.MapEffect(0x800375D2, 0x00020001, 0x22)),
            Is.EqualTo($"257|{Stamp}|800375D2|00020001|22|||{Hash}"));

    [Test]
    public void NameToggleNamesTheActorTwice()
        => Assert.That(Line(NetLogLine.NameToggle(Kefka, targetable: true)),
            Is.EqualTo($"34|{Stamp}|40001234|Kefka|40001234|Kefka|01|{Hash}"));

    [TestCase(true, "Interrupted")]
    [TestCase(false, "Cancelled")]
    public void CancelAbilitySaysWhy(bool interrupted, string reason)
        => Assert.That(Line(NetLogLine.CancelAbility(Kefka, 0xBAEF, "Stomp-a-Mole", interrupted)),
            Is.EqualTo($"23|{Stamp}|40001234|Kefka|BAEF|Stomp-a-Mole|{reason}|{Hash}"));

    [Test]
    public void WasDefeatedWithoutAKillerNamesNoSource()
        => Assert.That(Line(NetLogLine.WasDefeated(Machinist, NetLogActor.None)),
            Is.EqualTo($"25|{Stamp}|10FF0001|Machinist|E0000000||{Hash}"));

    [Test]
    public void ChangeZoneHasTheTerritoryInHex()
        => Assert.That(Line(NetLogLine.ChangeZone(1363, "The Dancing Plague")),
            Is.EqualTo($"01|{Stamp}|553|The Dancing Plague|{Hash}"));

    [Test]
    public void ActorSetPosHasTheHeadingThenThePosition()
        => Assert.That(Line(NetLogLine.ActorSetPos(Kefka.Id, -2.3563f, World)),
            Is.EqualTo($"271|{Stamp}|40001234|-2.3563|00|00|100.0000|90.0000|2.5000|{Hash}"));

    [Test]
    public void SpawnNpcExtraHasTheParentAndTether()
        => Assert.That(Line(NetLogLine.SpawnNpcExtra(Kefka.Id, NetLogLine.NoTarget, 0xF6, 1)),
            Is.EqualTo($"272|{Stamp}|40001234|E0000000|00F6|01|{Hash}"));

    [Test]
    public void ActorControlExtraHasTheCategoryAndParams()
        => Assert.That(Line(NetLogLine.ActorControlExtra(Kefka.Id, 0x3F, 0x6, 0, 0, 0)),
            Is.EqualTo($"273|{Stamp}|40001234|003F|6|0|0|0|{Hash}"));

    [Test]
    public void APipeInANameCantSplitTheLine()
        => Assert.That(Line(NetLogLine.HeadMarker(new NetLogActor(1, "A|B"), 1)).Split('|'), Has.Length.EqualTo(11));
}
