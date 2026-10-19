using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AnoMech.Core;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;

namespace AnoMech.Integrations.Iinact;

// What the sim sends its game client, also written as the ACT log lines the server's packets would
// have become, for IINACT's parser and cactbot. Installed over Natives at load, so whatever
// multiplayer installs later wraps it; every call reaches the game first, and lines are written
// only while `active` says so, for actors the game has. Spawns and despawns aren't written: IINACT
// finds combatants in the object table itself.
internal sealed class NetLogTap
{
    private const uint NoTarget = NetLogLine.NoTarget;
    private const uint CommenceCommand = 0x40000001;
    // Two hours, the time limit an ultimate's Commence carries.
    private const uint CommenceTimeLimit = 0x1C20;
    // A status list sends each timer as it stands; one that grew was reapplied.
    private const float RefreshTolerance = 0.5f;
    // ACT's duration for a status with no timer.
    private const float PermanentDuration = 9999f;

    private const uint DeathAnimationCategory = 0x0E;
    private const uint CancelCastCategory = 0x0F;
    private const uint HeadMarkerCategory = 0x22;
    private const uint SetTetherCategory = 0x23;
    private const uint TargetableCategory = 0x36;
    private const uint InterruptedFlag = 1;

    // The ActorControl categories OverlayPlugin writes as ActorControlExtra (273) lines.
    private static readonly HashSet<uint> ExtraCategories = [0x31, 0x3E, 0x3F, 0xB8, 0x197, 0x19D, 0x834];

    private readonly IBattleCharas realCharas;
    private readonly IEventObjects realObjects;
    private readonly IMapEffects realMap;
    private readonly IInstanceContentDirector realDirector;
    private readonly Action<NetLogEntry> write;
    private readonly Func<bool> active;
    private readonly Func<uint> instance;
    private readonly Dictionary<uint, TapChara> charas = [];
    private readonly TapChara localPlayer;
    private bool warned;

    private NetLogTap(Action<NetLogEntry> write, Func<bool> active, Func<uint> instance)
    {
        realCharas = Natives.BattleCharas;
        realObjects = Natives.EventObjects;
        realMap = Natives.MapEffects;
        realDirector = Natives.Director;
        this.write = write;
        this.active = active;
        this.instance = instance;
        localPlayer = new TapChara(realCharas.LocalPlayer, this);
    }

    public static NetLogTap Install(Action<NetLogEntry> write, Func<bool> active, Func<uint> instance)
    {
        var tap = new NetLogTap(write, active, instance);
        Natives.BattleCharas = new TapCharas(tap);
        Natives.EventObjects = new TapObjects(tap);
        Natives.MapEffects = new TapMapEffects(tap);
        Natives.Director = new TapDirector(tap);
        return tap;
    }

    public void Uninstall()
    {
        Natives.BattleCharas = realCharas;
        Natives.EventObjects = realObjects;
        Natives.MapEffects = realMap;
        Natives.Director = realDirector;
    }

    // ── Who is who ───────────────────────────────────────────────────────────

    private IBattleCharaProxy? Proxy(uint entityId)
    {
        if (entityId == localPlayer.EntityId) return localPlayer.Inner;
        return charas.TryGetValue(entityId, out var chara) ? chara.Inner : null;
    }

    private NetLogActor Actor(uint entityId)
    {
        if (entityId is 0 or NoTarget) return NetLogActor.None;
        return new NetLogActor(entityId, Proxy(entityId)?.Name ?? "");
    }

    private NetLogActor Actor(GameObjectId? id) => id is { } objectId ? Actor(objectId.ObjectId) : NetLogActor.None;

    private uint MaxHealthOf(uint entityId) => Proxy(entityId)?.MaxHealth ?? 0;

    // ── Lines ────────────────────────────────────────────────────────────────

    private bool Writes(TapChara chara) => active() && chara.Inner.Exists;

    // A line that can't be built is lost, never the call the sim made.
    private void Safely(Action writeLines)
    {
        try
        {
            writeLines();
        }
        catch (Exception e)
        {
            if (warned) return;
            warned = true;
            DiagnosticLog.Warn($"[NetLogTap] A log line couldn't be written; later failures go unlogged: {e}");
        }
    }

    private void Cast(TapChara chara, ActorCastData cast)
    {
        if (!Writes(chara)) return;
        var source = chara.Actor;
        // The server names the caster itself on a cast with no other target.
        var target = cast.Target is { } id ? Actor(id.ObjectId) : source;
        write(NetLogLine.StartsUsing(source, cast.ActionId, Natives.Data.ActionName(cast.ActionId), target, cast.CastTime,
            chara.Inner.Position, chara.Inner.Rotation));
        write(NetLogLine.StartsUsingExtra(source, cast.ActionId, cast.Position, cast.Rotation));
    }

    private void Effect(TapChara chara, uint sequence, ActionEffectData effect)
    {
        if (!Writes(chara)) return;
        var source = chara.Actor;
        var name = Natives.Data.ActionName(effect.ActionId);
        var inner = chara.Inner;
        var sourceHp = (inner.Health, inner.MaxHealth, inner.Mana, inner.MaxMana);
        var rotation = MathUtil.QuantizeRotation(effect.Rotation);
        var targets = effect.Targets ?? [];
        if (targets.Count == 0)
            write(NetLogLine.Ability(source, effect.ActionId, name, NetLogHit.Nobody, 0, 0, sourceHp, inner.Position, inner.Rotation,
                sequence, effect.SpellId, effect.AnimationLock, rotation));
        for (var i = 0; i < targets.Count; i++)
            write(NetLogLine.Ability(source, effect.ActionId, name, Hit(targets[i]), i, targets.Count, sourceHp, inner.Position, inner.Rotation,
                sequence, effect.SpellId, effect.AnimationLock, rotation));
        write(NetLogLine.AbilityExtra(source, effect.ActionId, sequence, effect.Position, effect.Rotation,
            effect.AnimationTarget?.ObjectId ?? NoTarget));
    }

    private NetLogHit Hit(ActionEffectTarget target)
    {
        var actor = Actor(target.Id);
        var effects = (target.Effects ?? []).Select(Words).ToList();
        return Proxy(actor.Id) is { } proxy
            ? new NetLogHit(actor, effects, proxy.Health, proxy.MaxHealth, proxy.Mana, proxy.MaxMana, proxy.Position, proxy.Rotation)
            : new NetLogHit(actor, effects, 0, 0, 0, 0, Vector3.Zero, 0f);
    }

    // The entry's 8 bytes as the two little-endian words ACT reads them as.
    private static (uint Flags, uint Damage) Words(ActionEffectEntry entry)
        => ((uint)entry.Type | (uint)entry.Param0 << 8 | (uint)entry.Param1 << 16 | (uint)entry.Param2 << 24,
            (uint)entry.Param3 | (uint)entry.Param4 << 8 | (uint)entry.Value << 16);

    private void Control(TapChara chara, uint category, uint[] args)
    {
        if (!Writes(chara)) return;
        var actor = chara.Actor;
        switch (category)
        {
            case HeadMarkerCategory:
                write(NetLogLine.HeadMarker(actor, args[0]));
                break;
            case SetTetherCategory:
                write(NetLogLine.Tether(actor, Actor(args[2]), args[1], args[3]));
                break;
            case CancelCastCategory:
                write(NetLogLine.CancelAbility(actor, args[2], Natives.Data.ActionName(args[2]), args[3] == InterruptedFlag));
                break;
            case DeathAnimationCategory:
                write(NetLogLine.WasDefeated(actor, NetLogActor.None));
                break;
            case TargetableCategory:
                write(NetLogLine.NameToggle(actor, args[0] != 0));
                break;
            default:
                if (ExtraCategories.Contains(category))
                    write(NetLogLine.ActorControlExtra(actor.Id, category, args[0], args[1], args[2], args[3]));
                break;
        }
    }

    private void ObjectControl(IEventObjectProxy obj, uint category, uint[] args)
    {
        if (!active() || !ExtraCategories.Contains(category)) return;
        write(NetLogLine.ActorControlExtra(obj.EntityId, category, args[0], args[1], args[2], args[3]));
    }

    private void Statuses(TapChara chara, IReadOnlyList<StatusSlot> statuses)
    {
        // Nothing to compare against once writing starts: the first list it sees is all gains.
        if (!active())
        {
            chara.Statuses = NoStatuses;
            return;
        }
        // The game drops it, so the next list is still compared against the last one it took.
        if (!chara.Inner.Exists) return;
        var now = new Dictionary<(ushort, uint), StatusSlot>();
        foreach (var slot in statuses)
            if (slot.StatusId != 0) now[(slot.StatusId, slot.SourceEntityId)] = slot;
        var before = chara.Statuses;
        chara.Statuses = now;
        var target = chara.Actor;
        var targetMaxHp = chara.Inner.MaxHealth;
        foreach (var (key, slot) in now)
        {
            if (before.TryGetValue(key, out var was) && slot.Param == was.Param && slot.RemainingTime <= was.RemainingTime + RefreshTolerance)
                continue;
            write(NetLogLine.GainsEffect(slot.StatusId, Natives.Data.StatusName(slot.StatusId),
                slot.RemainingTime > 0f ? slot.RemainingTime : PermanentDuration, Actor(slot.SourceEntityId), target, Stacks(slot.Param),
                targetMaxHp, MaxHealthOf(slot.SourceEntityId)));
        }
        foreach (var (key, was) in before)
        {
            if (now.ContainsKey(key)) continue;
            write(NetLogLine.LosesEffect(was.StatusId, Natives.Data.StatusName(was.StatusId), Actor(was.SourceEntityId), target,
                Stacks(was.Param), targetMaxHp, MaxHealthOf(was.SourceEntityId)));
        }
    }

    private static ushort Stacks(ushort param) => (ushort)(param & 0xFF);

    private static readonly Dictionary<(ushort, uint), StatusSlot> NoStatuses = [];

    private void Director(uint command, uint data0 = 0, uint data1 = 0, uint data2 = 0, uint data3 = 0)
    {
        if (!active()) return;
        write(NetLogLine.ActorControl(instance(), command, data0, data1, data2, data3));
    }

    private void MapEffect(uint flags, byte index)
    {
        if (!active()) return;
        write(NetLogLine.MapEffect(instance(), flags, index));
    }

    private IBattleCharaProxy? SpawnNpc(EnemySpawnConfig config, Placement placement, out uint entityId)
        => realCharas.SpawnBattleNpcFromPacket(config, placement, out entityId) is { } chara ? Track(chara, entityId) : null;

    private IBattleCharaProxy? SpawnDoppel(PartyMemberPreset preset, byte level, Placement placement, out uint entityId)
        => realCharas.SpawnDoppel(preset, level, placement, out entityId) is { } chara ? Track(chara, entityId) : null;

    private TapChara Track(IBattleCharaProxy chara, uint entityId)
    {
        var tapped = new TapChara(chara, this);
        charas[entityId] = tapped;
        return tapped;
    }

    // ── The tapped natives ───────────────────────────────────────────────────

    private sealed class TapCharas(NetLogTap tap) : IBattleCharas
    {
        public IBattleCharaProxy LocalPlayer => tap.localPlayer;

        public IBattleCharaProxy? SpawnBattleNpcFromPacket(EnemySpawnConfig config, Placement placement, out uint entityId)
            => tap.SpawnNpc(config, placement, out entityId);

        public IBattleCharaProxy? SpawnDoppel(PartyMemberPreset preset, byte level, Placement placement, out uint entityId)
            => tap.SpawnDoppel(preset, level, placement, out entityId);

        public bool IsInCharacterManager(uint entityId) => tap.realCharas.IsInCharacterManager(entityId);

        public void ReleaseSlot(int slot) => tap.realCharas.ReleaseSlot(slot);
    }

    private sealed class TapChara(IBattleCharaProxy inner, NetLogTap tap) : IBattleCharaProxy
    {
        public IBattleCharaProxy Inner => inner;
        public Dictionary<(ushort, uint), StatusSlot> Statuses { get; set; } = [];
        public NetLogActor Actor => new(inner.EntityId, inner.Name);

        public bool Exists => inner.Exists;
        public int Slot => inner.Slot;
        public uint EntityId => inner.EntityId;
        public GameObjectId GameObjectId => inner.GameObjectId;
        public string Name => inner.Name;
        public byte ClassJob => inner.ClassJob;
        public Vector3 Position => inner.Position;
        public float Rotation => inner.Rotation;
        public float HitboxRadius => inner.HitboxRadius;
        public bool HasDrawObject => inner.HasDrawObject;
        public bool IsDrawObjectVisible => inner.IsDrawObjectVisible;
        public byte ModelState => inner.ModelState;
        public bool IsCasting => inner.IsCasting;
        public uint CastActionId => inner.CastActionId;
        public float CurrentCastTime => inner.CurrentCastTime;
        public float TotalCastTime => inner.TotalCastTime;
        public uint Health => inner.Health;
        public uint MaxHealth => inner.MaxHealth;
        public uint Mana => inner.Mana;
        public uint MaxMana => inner.MaxMana;
        public byte TargetableStatus => inner.TargetableStatus;
        public ushort GetTetherId(byte slot) => inner.GetTetherId(slot);
        public string DescribeDrawState() => inner.DescribeDrawState();
        public string DescribeActionTimeline() => inner.DescribeActionTimeline();
        public string DescribeTimelineSlotIds() => inner.DescribeTimelineSlotIds();
        public string DescribeModelSlots() => inner.DescribeModelSlots();

        public void SetPos(Vector3 position, float rotation) => inner.SetPos(position, rotation);
        public void Move(Vector3 position, float rotation, ActorMoveFlags flags, float speed) => inner.Move(position, rotation, flags, speed);
        public void NpcKnockback(Vector3 position, float rotation, float durationSeconds) => inner.NpcKnockback(position, rotation, durationSeconds);
        public void ForcedFollow(uint? targetEntityId) => inner.ForcedFollow(targetEntityId);
        public void StopForcedMove() => inner.StopForcedMove();
        public void ReceiveEffectResult(uint globalSequence, byte targetIndex, uint health) => inner.ReceiveEffectResult(globalSequence, targetIndex, health);

        public void ReceiveActorCast(ActorCastData cast)
        {
            inner.ReceiveActorCast(cast);
            tap.Safely(() => tap.Cast(this, cast));
        }

        public uint ReceiveActionEffect(ActionEffectData effect)
        {
            var sequence = inner.ReceiveActionEffect(effect);
            tap.Safely(() => tap.Effect(this, sequence, effect));
            return sequence;
        }

        public void ActorControl(uint category, uint arg1 = 0, uint arg2 = 0, uint arg3 = 0, uint arg4 = 0, uint arg5 = 0, uint arg6 = 0, uint arg7 = 0, uint arg8 = 0)
        {
            inner.ActorControl(category, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8);
            tap.Safely(() => tap.Control(this, category, [arg1, arg2, arg3, arg4]));
        }

        public void ActorControlTarget(uint category, GameObjectId target, uint arg1 = 0, uint arg2 = 0, uint arg3 = 0, uint arg4 = 0)
            => inner.ActorControlTarget(category, target, arg1, arg2, arg3, arg4);

        public void ReceiveStatusList(IReadOnlyList<StatusSlot> statuses, uint health, uint mana, byte shield)
        {
            inner.ReceiveStatusList(statuses, health, mana, shield);
            tap.Safely(() => tap.Statuses(this, statuses));
        }

        public void Despawn()
        {
            tap.charas.Remove(inner.EntityId);
            inner.Despawn();
        }
    }

    private sealed class TapObjects(NetLogTap tap) : IEventObjects
    {
        public IEventObjectProxy? Spawn(EventObjectSpawnConfig config, Placement placement)
            => tap.realObjects.Spawn(config, placement) is { } obj ? new TapObject(obj, tap) : null;
    }

    private sealed class TapObject(IEventObjectProxy inner, NetLogTap tap) : IEventObjectProxy
    {
        public int Slot => inner.Slot;
        public uint EntityId => inner.EntityId;
        public Vector3 Position => inner.Position;
        public float Rotation => inner.Rotation;

        public void ActorControl(uint category, uint arg1 = 0, uint arg2 = 0, uint arg3 = 0, uint arg4 = 0, uint arg5 = 0, uint arg6 = 0, uint arg7 = 0, uint arg8 = 0)
        {
            inner.ActorControl(category, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8);
            tap.Safely(() => tap.ObjectControl(inner, category, [arg1, arg2, arg3, arg4]));
        }

        public void Despawn() => inner.Despawn();
    }

    private sealed class TapMapEffects(NetLogTap tap) : IMapEffects
    {
        public bool Loaded
        {
            get => tap.realMap.Loaded;
            set => tap.realMap.Loaded = value;
        }

        public bool Apply(uint packetFlags, byte index)
        {
            if (!tap.realMap.Apply(packetFlags, index)) return false;
            tap.Safely(() => tap.MapEffect(packetFlags, index));
            return true;
        }

        public bool ApplySnapshot(ReadOnlySpan<ushort> states, bool zoneIn) => tap.realMap.ApplySnapshot(states, zoneIn);

        public void LogAllSlots(string label) => tap.realMap.LogAllSlots(label);

        public void Dispose() => tap.realMap.Dispose();
    }

    private sealed class TapDirector(NetLogTap tap) : IInstanceContentDirector
    {
        public bool ProcessDirectorUpdate(uint category, uint arg1 = 0, uint arg2 = 0, uint arg3 = 0, uint arg4 = 0, uint arg5 = 0, uint arg6 = 0)
        {
            if (!tap.realDirector.ProcessDirectorUpdate(category, arg1, arg2, arg3, arg4, arg5, arg6)) return false;
            tap.Safely(() => tap.Director(category, arg1, arg2, arg3, arg4));
            return true;
        }

        public void SetDirectorData(byte sequence, byte unknown, byte[] unionData) => tap.realDirector.SetDirectorData(sequence, unknown, unionData);

        public void Commence()
        {
            tap.realDirector.Commence();
            tap.Safely(() => tap.Director(CommenceCommand, CommenceTimeLimit));
        }
    }
}
