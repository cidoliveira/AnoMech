using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using AnoMech.Core;

namespace AnoMech.Integrations.Iinact;

// Feeds a sim stay to IINACT, so cactbot's triggers and callouts run on the sim's fight: the
// NetLogTap's lines for what the sim does, plus the zone changes and pull ends a real stay's
// packets would have given IINACT's parser. Combat state, party and combatants IINACT reads from
// game memory, which the sim and SplatoonCompat already set.
internal sealed class IinactBridge : IDisposable
{
    public const string IinactInternalName = "IINACT";

    private const uint WipeCommand = 0x4000000F;

    // cactbot starts a pull afresh, dropping everything it gathered, when it sees combat begin: up
    // to a 300 ms poll after the condition rises with the game unfocused. A run's lines wait that long.
    private const float CactbotPollSeconds = 0.4f;

    // IINACT's log thread checks the log file switch per line as it drains its queue, every 50 ms
    // when idle: the switch goes back only once the stay's last lines are through.
    private const float RestoreDelaySeconds = 2f;
    private const double UnloadDrainSeconds = 0.25;

    // cactbot's TOP triggers take the first head marker of a pull for the fight's first, 0x0017,
    // and offset every later one by the difference, so a run starting mid-fight sends that marker
    // first. On no one: TOP's tank trigger still calls it once, at the start.
    private static readonly Dictionary<uint, uint> FirstHeadMarkers = new() { [1122] = 0x0017 };

    private readonly Func<(INetLogSink? Sink, string? Missing)> findSink;
    private readonly Func<uint, string> territoryName;
    private readonly Func<uint> instance;
    private readonly Action<TimeSpan> sleep;

    private INetLogSink? sink;
    private bool inStay;
    private bool? keptOffDiskBefore;
    private float restoreIn;
    private long lastSentAt;

    private bool muted;
    private bool holding;
    private float engagedFor;
    private uint? firstHeadMarker;
    private readonly List<NetLogEntry> held = [];

    public IinactBridge(Func<(INetLogSink? Sink, string? Missing)> findSink, Func<uint, string> territoryName, Func<uint> instance,
        Action<TimeSpan>? sleep = null)
    {
        this.findSink = findSink;
        this.territoryName = territoryName;
        this.instance = instance;
        this.sleep = sleep ?? Thread.Sleep;
    }

    public bool Writing { get; private set; }
    public string? LastResult { get; private set; }
    public int LinesWritten { get; private set; }

    public static bool IsIinactLoaded()
        => Plugin.PluginInterface.InstalledPlugins.Any(p => p.InternalName == IinactInternalName && p.IsLoaded);

    // Every frame. inSimStay: a sim zone is loaded and the bridge is on.
    public void Tick(float deltaSeconds, bool inSimStay, bool combatEngaged, uint simTerritory, uint clientTerritory)
    {
        if (inSimStay && !inStay) BeginStay(simTerritory);
        else if (!inSimStay && inStay) EndStay(clientTerritory);
        if (holding && combatEngaged && (engagedFor += deltaSeconds) >= CactbotPollSeconds) Release();
        if (restoreIn > 0f && (restoreIn -= deltaSeconds) <= 0f) RestoreDisk();
    }

    // The tap's lines.
    public void Write(NetLogEntry entry)
    {
        if (!Writing || muted) return;
        if (holding) held.Add(entry);
        else Send(entry);
    }

    public void OnScenarioStarted(uint territory)
    {
        if (!Writing) return;
        held.Clear();
        holding = true;
        engagedFor = 0f;
        firstHeadMarker = FirstHeadMarkers.TryGetValue(territory, out var marker) ? marker : null;
    }

    // Before the run's teardown, whose despawns and cleared statuses aren't the fight's.
    public void OnScenarioStopping()
    {
        muted = true;
        holding = false;
        held.Clear();
    }

    // After the teardown: cactbot ends the pull on the director's wipe.
    public void OnScenarioStopped()
    {
        muted = false;
        if (Writing) Send(NetLogLine.ActorControl(instance(), WipeCommand));
    }

    // At unload, before the sim's own teardown, so none of it is written.
    public void StopWriting()
    {
        Writing = false;
        holding = false;
        held.Clear();
    }

    private void BeginStay(uint simTerritory)
    {
        inStay = true;
        restoreIn = 0f;
        if (sink is null)
        {
            var (found, missing) = findSink();
            if (found is null)
            {
                LastResult = missing;
                return;
            }
            sink = found;
        }
        try
        {
            keptOffDiskBefore ??= sink.KeepOffDisk;
            sink.KeepOffDisk = true;
        }
        catch (Exception e)
        {
            DiagnosticLog.Warn($"[IinactBridge] Couldn't keep the sim off IINACT's log file, so nothing is sent: {e}");
            LastResult = "couldn't keep the sim off IINACT's log file (see the log)";
            sink = null;
            keptOffDiskBefore = null;
            return;
        }
        Writing = true;
        muted = false;
        LinesWritten = 0;
        LastResult = "sending the sim";
        // IINACT's parser only learns a zone from its own memory reading, which the sim's load
        // doesn't move.
        Send(NetLogLine.ChangeZone(simTerritory, territoryName(simTerritory)));
    }

    private void EndStay(uint clientTerritory)
    {
        inStay = false;
        if (!Writing) return;
        Send(NetLogLine.ChangeZone(clientTerritory, territoryName(clientTerritory)));
        StopWriting();
        LastResult = $"sent {LinesWritten} lines";
        restoreIn = RestoreDelaySeconds;
    }

    private void Release()
    {
        holding = false;
        if (firstHeadMarker is { } marker) Send(NetLogLine.HeadMarker(NetLogActor.None, marker));
        foreach (var entry in held) Send(entry);
        held.Clear();
    }

    private void Send(NetLogEntry entry)
    {
        if (sink is null) return;
        try
        {
            sink.Write(entry);
            LinesWritten++;
            lastSentAt = Stopwatch.GetTimestamp();
        }
        catch (Exception e)
        {
            DiagnosticLog.Warn($"[IinactBridge] IINACT refused a line, so nothing more is sent this stay: {e}");
            LastResult = "IINACT stopped taking lines (see the log)";
            StopWriting();
            restoreIn = RestoreDelaySeconds;
        }
    }

    private void RestoreDisk()
    {
        restoreIn = 0f;
        if (sink is not null && keptOffDiskBefore is { } before)
        {
            try { sink.KeepOffDisk = before; }
            catch (Exception e) { DiagnosticLog.Warn($"[IinactBridge] Couldn't put IINACT's log file switch back: {e}"); }
        }
        keptOffDiskBefore = null;
        sink = null;
    }

    public void Dispose()
    {
        StopWriting();
        if (keptOffDiskBefore is not null)
        {
            var wait = TimeSpan.FromSeconds(UnloadDrainSeconds) - Stopwatch.GetElapsedTime(lastSentAt);
            if (wait > TimeSpan.Zero) sleep(wait);
        }
        RestoreDisk();
    }
}
