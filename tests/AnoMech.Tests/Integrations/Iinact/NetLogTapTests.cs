using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;
using AnoMech.Integrations.Iinact;
using FFXIVClientStructs.FFXIV.Client.Game;
using BNpcBaseId = AnoMech.Scenarios.Umad.UmadConstants.BNpcBaseId;
using BNpcNameId = AnoMech.Scenarios.Umad.UmadConstants.BNpcNameId;

namespace AnoMech.Tests;

public class NetLogTapTests
{
    private const uint Instance = 0x800375D2;

    private readonly List<string> lines = [];
    private bool active;
    private FakeGame game = null!;
    private NetLogTap tap = null!;

    [SetUp]
    public void SetUp()
    {
        lines.Clear();
        active = true;
        game = FakeGame.Install();
        tap = NetLogTap.Install(entry => lines.Add(entry.ToLine(DateTimeOffset.Now)), () => active, () => Instance);
    }

    [TearDown]
    public void TearDown() => tap.Uninstall();

    private static IBattleCharaProxy Player => Natives.BattleCharas.LocalPlayer;
    private static string Id(uint entityId) => entityId.ToString("X8");
    private IEnumerable<string> Types => lines.Select(line => line.Split('|')[0]);
    private string[] Fields(string type) => lines.Single(line => line.StartsWith(type + "|")).Split('|');

    private IBattleCharaProxy SpawnKefka()
    {
        var kefka = Natives.BattleCharas.SpawnBattleNpcFromPacket(
            new EnemySpawnConfig(BNpcBaseId.KefkaP3, BNpcNameId.Kefka, Level: 100), new Placement(new Vector3(100f, 0f, 90f), 0f), out _)!;
        for (var frame = 0; frame < 30 && !kefka.Exists; frame++) game.BattleCharas.Tick(1f / 60f);
        lines.Clear();
        return kefka;
    }

    [Test]
    public void ACastIsStartsUsingThenItsLocation()
    {
        var kefka = SpawnKefka();

        kefka.ReceiveActorCast(new ActorCastData(0xBAEF, ActionType.Action, 5f, 0f, false, 1f, new Vector3(100f, 0f, 100f), Player.GameObjectId));

        Assert.Multiple(() =>
        {
            Assert.That(Types, Is.EqualTo(new[] { "20", "263" }));
            Assert.That(Fields("20")[2..8], Is.EqualTo(new[] { Id(kefka.EntityId), kefka.Name, "BAEF", Natives.Data.ActionName(0xBAEF), Id(Player.EntityId), Player.Name }));
            Assert.That(Fields("263")[4..7], Is.EqualTo(new[] { "100.000", "100.000", "0.000" }));
        });
    }

    [Test]
    public void ACastWithNoTargetIsOnTheCaster()
    {
        var kefka = SpawnKefka();

        kefka.ReceiveActorCast(new ActorCastData(0xBAEF, ActionType.Action, 5f, 0f, false, 0f, Vector3.Zero));

        Assert.That(Fields("20")[6], Is.EqualTo(Id(kefka.EntityId)));
    }

    [Test]
    public void AnEffectIsOneAbilityPerTargetThenItsExtra()
    {
        var kefka = SpawnKefka();
        var hit = ActionEffectEntry.Damage(1234, 5);

        kefka.ReceiveActionEffect(new ActionEffectData(0xBAF0, ActionType.Action, 0.6f, 0xBAF0, 0, 0, 0f, Vector3.Zero,
            Targets: [new ActionEffectTarget(Player.GameObjectId, [hit]), new ActionEffectTarget(kefka.GameObjectId)]));

        Assert.Multiple(() =>
        {
            Assert.That(Types, Is.EqualTo(new[] { "22", "22", "264" }));
            var first = lines[0].Split('|');
            Assert.That(first[6], Is.EqualTo(Id(Player.EntityId)));
            Assert.That(first[8], Is.EqualTo(((uint)hit.Type | (uint)hit.Param0 << 8 | (uint)hit.Param1 << 16 | (uint)hit.Param2 << 24).ToString("X")));
            Assert.That(first[9], Is.EqualTo(((uint)hit.Param3 | (uint)hit.Param4 << 8 | (uint)hit.Value << 16).ToString("X")));
            Assert.That(first[45..47], Is.EqualTo(new[] { "0", "2" }));
        });
    }

    [Test]
    public void AnEffectThatHitNobodyIsStillAnAbility()
    {
        var kefka = SpawnKefka();

        kefka.ReceiveActionEffect(new ActionEffectData(0xBAF0, ActionType.Action, 0.6f, 0xBAF0, 0, 0, 0f, Vector3.Zero));

        Assert.That(Types, Is.EqualTo(new[] { "21", "264" }));
        Assert.That(Fields("21")[6], Is.EqualTo("E0000000"));
    }

    [Test]
    public void HeadMarkersAndTethersNameWhoTheyAreOn()
    {
        var kefka = SpawnKefka();

        Player.ActorControl(0x22, 0x157, Player.EntityId);
        kefka.ActorControl(0x23, 0, 0xF6, Player.EntityId, 15);

        Assert.Multiple(() =>
        {
            Assert.That(Fields("27")[2..7], Is.EqualTo(new[] { Id(Player.EntityId), Player.Name, "0000", "0000", "0157" }));
            Assert.That(Fields("35")[2..9], Is.EqualTo(new[] { Id(kefka.EntityId), kefka.Name, Id(Player.EntityId), Player.Name, "0000", "0000", "00F6" }));
        });
    }

    [Test]
    public void ModelStatesAreActorControlExtraButWeaponDrawsAreNot()
    {
        var kefka = SpawnKefka();

        kefka.ActorControl(0x3F, 6);
        kefka.ActorControl(0x00, 1, 1);

        Assert.That(lines, Has.Count.EqualTo(1));
        Assert.That(Fields("273")[2..5], Is.EqualTo(new[] { Id(kefka.EntityId), "003F", "6" }));
    }

    [Test]
    public void CancelDeathAndTargetableHaveTheirOwnLines()
    {
        var kefka = SpawnKefka();

        kefka.ActorControl(0x0F, 540, 1, 0xBAEF, 1);
        kefka.ActorControl(0x0E);
        kefka.ActorControl(0x36, 0);

        Assert.Multiple(() =>
        {
            Assert.That(Fields("23")[4..7], Is.EqualTo(new[] { "BAEF", Natives.Data.ActionName(0xBAEF), "Interrupted" }));
            Assert.That(Fields("25")[2..5], Is.EqualTo(new[] { Id(kefka.EntityId), kefka.Name, "E0000000" }));
            Assert.That(Fields("34")[6], Is.EqualTo("00"));
        });
    }

    [Test]
    public void StatusListsBecomeGainsAndLosesAsTheyChange()
    {
        var kefka = SpawnKefka();
        var forsaken = new StatusSlot(0x13DC, 0, 20f, kefka.EntityId);

        Player.ReceiveStatusList([forsaken], 1000, 0, 0);
        Player.ReceiveStatusList([forsaken with { RemainingTime = 19f }], 1000, 0, 0);
        Player.ReceiveStatusList([forsaken with { RemainingTime = 30f }], 1000, 0, 0);
        Player.ReceiveStatusList([], 1000, 0, 0);

        Assert.Multiple(() =>
        {
            Assert.That(Types, Is.EqualTo(new[] { "26", "26", "30" }));
            Assert.That(lines[0].Split('|')[2..9], Is.EqualTo(new[] { "13DC", Natives.Data.StatusName(0x13DC), "20.00", Id(kefka.EntityId), kefka.Name, Id(Player.EntityId), Player.Name }));
            Assert.That(lines[1].Split('|')[4], Is.EqualTo("30.00"));
        });
    }

    [Test]
    public void TheDirectorAndMapEffectsCarryTheInstance()
    {
        Natives.Zone.Enter(1363, Vector3.Zero);
        Natives.MapEffects.Loaded = true;

        Natives.Director.ProcessDirectorUpdate(0x80000027, 0x1B, 0x2, 0x1BDB, 0x40004141);
        Natives.Director.Commence();
        Natives.MapEffects.Apply(0x00020001, 0x22);

        Assert.That(lines, Is.EqualTo(new[]
        {
            Strip(lines[0], "33|", "|800375D2|80000027|1B|02|1BDB|40004141|"),
            Strip(lines[1], "33|", "|800375D2|40000001|1C20|00|00|00|"),
            Strip(lines[2], "257|", "|800375D2|00020001|22|||"),
        }));
    }

    [Test]
    public void AnActorTheGameDoesNotHaveYetGetsNoLinesAndKeepsItsStatusBaseline()
    {
        game.BattleCharas.PacketSpawnFrames = (5, 5);
        var kefka = Natives.BattleCharas.SpawnBattleNpcFromPacket(
            new EnemySpawnConfig(BNpcBaseId.KefkaP3, BNpcNameId.Kefka, Level: 100), new Placement(Vector3.Zero, 0f), out _)!;
        var status = new StatusSlot(0x13DC, 0, 20f, 0xE0000000);

        kefka.ReceiveActorCast(new ActorCastData(0xBAEF, ActionType.Action, 5f, 0f, false, 0f, Vector3.Zero));
        kefka.ReceiveStatusList([status], 0, 0, 0);
        Assert.That(lines, Is.Empty);

        for (var frame = 0; frame < 30 && !kefka.Exists; frame++) game.BattleCharas.Tick(1f / 60f);
        kefka.ReceiveStatusList([status], 0, 0, 0);

        Assert.That(Types, Is.EqualTo(new[] { "26" }));
    }

    [Test]
    public void ALineThatFailsNeverStopsTheCall()
    {
        var kefka = SpawnKefka();
        tap.Uninstall();
        tap = NetLogTap.Install(_ => throw new InvalidOperationException("sink down"), () => true, () => Instance);
        var tapped = Natives.BattleCharas.SpawnBattleNpcFromPacket(
            new EnemySpawnConfig(BNpcBaseId.KefkaP3, BNpcNameId.Kefka, Level: 100), new Placement(Vector3.Zero, 0f), out _)!;
        for (var frame = 0; frame < 30 && !tapped.Exists; frame++) game.BattleCharas.Tick(1f / 60f);

        Assert.DoesNotThrow(() => tapped.ReceiveActorCast(new ActorCastData(0xBAEF, ActionType.Action, 5f, 0f, false, 0f, Vector3.Zero)));
        Assert.That(tapped.IsCasting);
        Assert.That(kefka.Exists);
    }

    [Test]
    public void NothingIsWrittenWhileInactiveButTheGameStillGetsTheCall()
    {
        var kefka = SpawnKefka();
        active = false;

        kefka.ReceiveActorCast(new ActorCastData(0xBAEF, ActionType.Action, 5f, 0f, false, 0f, Vector3.Zero));

        Assert.That(lines, Is.Empty);
        Assert.That(kefka.IsCasting);
    }

    // The line, if it has the expected type and body; the timestamp between them is whatever the clock said.
    private static string Strip(string line, string type, string body)
        => line.StartsWith(type) && line.EndsWith(body + "0000000000000000") ? line : $"{type}<timestamp>{body}<hash>";
}
