using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace AnoMech.Integrations.BossMod;

// Moves BossMod's fight module to where a scenario starts (see BossModStart), so its hints and
// everything built on them (NyaDraw's BossMod AOEs) cover the practiced mechanic. BossMod
// creates and starts the module on its own once the primary boss is in combat; this waits for
// that, then transitions it once.
internal sealed class BossModBridge
{
    private const float GiveUpSeconds = 15f;

    private BossModStart? pending;
    private float waited;

    public string? LastResult { get; private set; }

    public void OnScenarioStarted(BossModStart? start)
    {
        pending = Plugin.Config.SplatoonCompat && Plugin.Config.SyncBossModModule ? start : null;
        waited = 0f;
        if (pending != null) LastResult = $"waiting for BossMod's {ShortName(pending.ModuleType)} module";
    }

    public void OnScenarioStopped() => pending = null;

    // Every frame, after Game.Tick.
    public void Tick(float deltaSeconds)
    {
        if (pending is not { } start) return;
        waited += deltaSeconds;
        if (waited > GiveUpSeconds)
        {
            Finish($"gave up: no started {ShortName(start.ModuleType)} module after {GiveUpSeconds:F0}s; saw {BossModReflection.LastScan}");
            return;
        }

        var synced = new List<string>();
        foreach (var (plugin, manager) in BossModReflection.FindManagers())
        {
            try
            {
                if (TrySync(manager, start)) synced.Add(plugin);
            }
            catch (Exception e)
            {
                Finish($"{plugin}: moving the module threw, left as BossMod set it: {e.InnerException?.Message ?? e.Message}");
                return;
            }
        }
        if (synced.Count > 0)
            Finish($"{string.Join(" and ", synced)} {ShortName(start.ModuleType)} moved to phase {start.Phase}, state 0x{start.StatePath[^1]:X}");
    }

    // False while the module isn't loaded or its state machine hasn't started yet.
    internal static bool TrySync(object manager, BossModStart start)
    {
        if (BossModReflection.Read(manager, "LoadedModules") is not IEnumerable modules) return false;
        var module = modules.Cast<object>().FirstOrDefault(m => m.GetType().FullName == start.ModuleType);
        if (module == null || BossModReflection.Read(module, "StateMachine") is not { } machine) return false;
        if (BossModReflection.Read(machine, "ActivePhaseIndex") is not int phaseIndex || phaseIndex < 0) return false;

        if (BossModReflection.Read(machine, "Phases") is not IList phases || start.Phase >= phases.Count)
            throw new InvalidOperationException($"the module has no phase {start.Phase}");
        var initial = BossModReflection.Read(phases[start.Phase]!, "InitialState")
                      ?? throw new InvalidOperationException($"phase {start.Phase} has no initial state");
        var states = start.StatePath
            .Select(id => FindState(initial, id) ?? throw new InvalidOperationException($"no state 0x{id:X} in phase {start.Phase}"))
            .ToList();

        BossModReflection.Invoke(machine, "TransitionToPhase", start.Phase);
        foreach (var state in states)
            BossModReflection.Invoke(machine, "TransitionToState", state);
        return true;
    }

    internal static object? FindState(object initial, uint id)
    {
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var queue = new Queue<object>([initial]);
        while (queue.Count > 0)
        {
            var state = queue.Dequeue();
            if (!seen.Add(state)) continue;
            if (BossModReflection.Read(state, "ID") is uint stateId && stateId == id) return state;
            if (BossModReflection.Read(state, "NextStates") is IEnumerable next)
                foreach (var successor in next)
                    if (successor != null) queue.Enqueue(successor);
        }
        return null;
    }

    private void Finish(string result)
    {
        LastResult = result;
        pending = null;
        Core.DiagnosticLog.Info($"[BossModBridge] {result}.");
    }

    private static string ShortName(string moduleType) => moduleType[(moduleType.LastIndexOf('.') + 1)..];
}
