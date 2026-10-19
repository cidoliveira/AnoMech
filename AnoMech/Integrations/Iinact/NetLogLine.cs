using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;

namespace AnoMech.Integrations.Iinact;

internal readonly record struct NetLogActor(uint Id, string Name)
{
    public static readonly NetLogActor None = new(NetLogLine.NoTarget, "");
}

// One target of an Ability line. Effects are the target's raw 8-byte effect entries, each split
// into the two words ACT writes (type and params, then params and value).
internal readonly record struct NetLogHit(
    NetLogActor Target, IReadOnlyList<(uint Flags, uint Damage)> Effects,
    uint Hp, uint MaxHp, uint Mp, uint MaxMp, Vector3 Position, float Heading)
{
    public static readonly NetLogHit Nobody = new(NetLogActor.None, [], 0, 0, 0, 0, Vector3.Zero, 0f);
}

// One ACT network log line without the timestamp and hash, which ACT's log writer adds: the type
// and the fields between them.
internal readonly record struct NetLogEntry(ushort Type, string Body)
{
    // The whole line as the log writer puts it out, but for the hash, which nothing checks.
    public string ToLine(DateTimeOffset at)
        => $"{Type:D2}|{at.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffffzzz", CultureInfo.InvariantCulture)}|{Body}|0000000000000000";
}

// ACT network log lines, FFXIV_ACT_Plugin's and OverlayPlugin's, in the field layout cactbot's
// netlog_defs reads. ACT writes positions as (x, ground-plane y, height z): the game's Z is ACT's y.
internal static class NetLogLine
{
    public const uint NoTarget = 0xE0000000;
    private const int EffectSlots = 8;

    public static NetLogEntry ChangeZone(uint territoryId, string name) => Entry(1, Hex(territoryId), Clean(name));

    public static NetLogEntry StartsUsing(NetLogActor source, uint actionId, string actionName, NetLogActor target, float castTime,
        Vector3 position, float heading)
        => Entry(20, Id(source.Id), Clean(source.Name), Hex(actionId), Clean(actionName), Id(target.Id), Clean(target.Name),
            F2(castTime), Position(position), F3(heading));

    // 22 when the effect hit more than one target, one line per target, as ACT splits it.
    public static NetLogEntry Ability(NetLogActor source, uint actionId, string actionName, NetLogHit hit, int targetIndex,
        int targetCount, (uint Hp, uint MaxHp, uint Mp, uint MaxMp) sourceHp, Vector3 sourcePosition, float sourceHeading,
        uint sequence, uint animationId, float animationLock, ushort rotation)
    {
        var fields = new List<string> { Id(source.Id), Clean(source.Name), Hex(actionId), Clean(actionName), Id(hit.Target.Id), Clean(hit.Target.Name) };
        for (var i = 0; i < EffectSlots; i++)
        {
            var (flags, damage) = i < hit.Effects.Count ? hit.Effects[i] : (0u, 0u);
            fields.Add(Hex(flags));
            fields.Add(Hex(damage));
        }
        fields.AddRange([Dec(hit.Hp), Dec(hit.MaxHp), Dec(hit.Mp), Dec(hit.MaxMp), "0", "1000", Position(hit.Position), F3(hit.Heading)]);
        fields.AddRange([Dec(sourceHp.Hp), Dec(sourceHp.MaxHp), Dec(sourceHp.Mp), Dec(sourceHp.MaxMp), "0", "1000", Position(sourcePosition), F3(sourceHeading)]);
        fields.AddRange([Id(sequence), Dec((uint)targetIndex), Dec((uint)targetCount), Id(NoTarget), "", "01", Hex(actionId), Hex(animationId),
            F3(animationLock), rotation.ToString("X4", CultureInfo.InvariantCulture)]);
        return Entry(targetCount > 1 ? (ushort)22 : (ushort)21, [.. fields]);
    }

    public static NetLogEntry CancelAbility(NetLogActor source, uint actionId, string actionName, bool interrupted)
        => Entry(23, Id(source.Id), Clean(source.Name), Hex(actionId), Clean(actionName), interrupted ? "Interrupted" : "Cancelled");

    public static NetLogEntry WasDefeated(NetLogActor target, NetLogActor source)
        => Entry(25, Id(target.Id), Clean(target.Name), Id(source.Id), Clean(source.Name));

    public static NetLogEntry GainsEffect(ushort statusId, string statusName, float duration, NetLogActor source, NetLogActor target,
        ushort stacks, uint targetMaxHp, uint sourceMaxHp)
        => Entry(26, Hex(statusId), Clean(statusName), F2(duration), Id(source.Id), Clean(source.Name), Id(target.Id), Clean(target.Name),
            Hex2(stacks), Dec(targetMaxHp), Dec(sourceMaxHp));

    public static NetLogEntry HeadMarker(NetLogActor target, uint lockonId)
        => Entry(27, Id(target.Id), Clean(target.Name), "0000", "0000", Hex4(lockonId), "0000", "0000", "0000");

    public static NetLogEntry LosesEffect(ushort statusId, string statusName, NetLogActor source, NetLogActor target, ushort stacks,
        uint targetMaxHp, uint sourceMaxHp)
        => Entry(30, Hex(statusId), Clean(statusName), "0.00", Id(source.Id), Clean(source.Name), Id(target.Id), Clean(target.Name),
            Hex2(stacks), Dec(targetMaxHp), Dec(sourceMaxHp));

    // The director's update, or Commence/wipe (commands 40000001 / 4000000F).
    public static NetLogEntry ActorControl(uint instance, uint command, uint data0 = 0, uint data1 = 0, uint data2 = 0, uint data3 = 0)
        => Entry(33, Id(instance), Id(command), Hex2(data0), Hex2(data1), Hex2(data2), Hex2(data3));

    public static NetLogEntry NameToggle(NetLogActor actor, bool targetable)
        => Entry(34, Id(actor.Id), Clean(actor.Name), Id(actor.Id), Clean(actor.Name), targetable ? "01" : "00");

    public static NetLogEntry Tether(NetLogActor source, NetLogActor target, uint tetherId, uint param)
        => Entry(35, Id(source.Id), Clean(source.Name), Id(target.Id), Clean(target.Name), "0000", "0000", Hex4(tetherId), Id(target.Id),
            Hex4(param), "0000");

    // OverlayPlugin's own line leaves the two trailing data fields empty.
    public static NetLogEntry MapEffect(uint instance, uint flags, byte location)
        => Entry(257, Id(instance), Id(flags), Hex2(location), "", "");

    public static NetLogEntry StartsUsingExtra(NetLogActor source, uint actionId, Vector3 position, float heading)
        => Entry(263, Id(source.Id), Hex4(actionId), Position(position), F3(heading));

    public static NetLogEntry AbilityExtra(NetLogActor source, uint actionId, uint globalEffectCounter, Vector3 position, float heading,
        uint animationTargetId)
        => Entry(264, Id(source.Id), Hex4(actionId), Id(globalEffectCounter), "1", Position(position), F3(heading), Id(animationTargetId));

    public static NetLogEntry ActorControlExtra(uint actorId, uint category, uint param1, uint param2, uint param3, uint param4)
        => Entry(273, Id(actorId), Hex4(category), Hex(param1), Hex(param2), Hex(param3), Hex(param4));

    private static NetLogEntry Entry(ushort type, params string[] fields) => new(type, string.Join('|', fields));

    private static string Position(Vector3 world) => $"{F3(world.X)}|{F3(world.Z)}|{F3(world.Y)}";

    private static string Clean(string text) => text.Replace('|', '/');
    private static string Id(uint value) => value.ToString("X8", CultureInfo.InvariantCulture);
    private static string Hex(uint value) => value.ToString("X", CultureInfo.InvariantCulture);
    private static string Hex2(uint value) => value.ToString("X2", CultureInfo.InvariantCulture);
    private static string Hex4(uint value) => value.ToString("X4", CultureInfo.InvariantCulture);
    private static string Dec(uint value) => value.ToString(CultureInfo.InvariantCulture);
    private static string F2(float value) => value.ToString("F2", CultureInfo.InvariantCulture);
    private static string F3(float value) => value.ToString("F3", CultureInfo.InvariantCulture);
}
