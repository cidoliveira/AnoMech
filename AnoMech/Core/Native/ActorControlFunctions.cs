using System;
using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using Lumina.Excel.Sheets;

namespace AnoMech.Core.Native;

// Delivers head markers and tethers through the client's ActorControl packet handler, the path a
// server ActorControl takes. The handler applies them natively (the lockon VFX, the VfxContainer
// tether) and every plugin hooking it sees the event: BossMod and NyaDraw read markers and
// tethers only from there, so the direct VFX/tether calls were invisible to them.
//
// UNVERIFIED in game: the parameter layout follows BossMod's packet decoder (TargetIcon p1 =
// lockon id; Tether p1 = slot, p2 = channeling id, p3 = target entity id, p4 = the byte
// SetTether receives last; TetherCancel p1 = slot, p2 = channeling id).
internal static unsafe class ActorControlFunctions
{
    private const uint TargetIconCategory = 34;
    private const uint TetherCategory = 35;
    private const uint TetherCancelCategory = 47;
    private const uint InvalidEntityId = 0xE0000000;

    // The call target of this site is the handler Splatoon and BossMod hook.
    private const string HandlerSignature = "E8 ?? ?? ?? ?? 0F B7 0B 83 E9 64";

    private static delegate* unmanaged<uint, uint, uint, uint, uint, uint, uint, uint, uint, uint, ulong, byte, void> handler;
    private static bool resolved;
    private static Dictionary<string, uint>? lockonIdsByPath;

    // False when routing is off, the handler wasn't found, or the handler couldn't resolve the
    // actor: it looks the source up by entity id, so an unregistered carrier gets nothing.
    public static bool CanDeliver(uint entityId)
    {
        if (!Plugin.Config.SplatoonCompat || !Plugin.Config.RouteMarkersAndTethers) return false;
        if (entityId is 0 or InvalidEntityId || !TryResolve()) return false;
        var characters = CharacterManager.Instance();
        return characters != null && characters->LookupBattleCharaByEntityId(entityId) != null;
    }

    public static void TargetIcon(uint entityId, uint lockonId)
        => handler(entityId, TargetIconCategory, lockonId, 0, 0, 0, 0, 0, 0, 0, InvalidEntityId, 0);

    public static void Tether(uint sourceEntityId, byte slot, ushort tetherId, uint targetEntityId, byte param)
        => handler(sourceEntityId, TetherCategory, slot, tetherId, targetEntityId, param, 0, 0, 0, 0, InvalidEntityId, 0);

    public static void TetherCancel(uint sourceEntityId, byte slot, ushort tetherId)
        => handler(sourceEntityId, TetherCancelCategory, slot, tetherId, 0, 0, 0, 0, 0, 0, InvalidEntityId, 0);

    // The Lockon row whose icon a "vfx/lockon/eff/<icon>.avfx" path plays. Several rows can
    // share an icon; the lowest id wins, so a module keyed on a later duplicate won't match.
    public static uint? LockonIdForPath(string path)
    {
        lockonIdsByPath ??= BuildLockonIndex();
        return lockonIdsByPath.TryGetValue(path, out var id) ? id : null;
    }

    private static Dictionary<string, uint> BuildLockonIndex()
    {
        var index = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in Plugin.DataManager.GetExcelSheet<Lockon>())
        {
            var icon = row.IconName.ExtractText();
            if (icon.Length > 0) index.TryAdd($"vfx/lockon/eff/{icon}.avfx", row.RowId);
        }
        return index;
    }

    private static bool TryResolve()
    {
        if (resolved) return handler != null;
        resolved = true;
        try
        {
            if (Plugin.SigScanner.TryScanText(HandlerSignature, out var address))
                handler = (delegate* unmanaged<uint, uint, uint, uint, uint, uint, uint, uint, uint, uint, ulong, byte, void>)address;
            else
                DiagnosticLog.Warn("[ActorControl] Handler signature not found; markers and tethers stay on the direct path.");
        }
        catch (Exception e)
        {
            DiagnosticLog.Warn($"[ActorControl] Handler lookup threw; markers and tethers stay on the direct path. {e}");
        }
        return handler != null;
    }
}
