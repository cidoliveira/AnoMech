using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;

namespace AnoMech.Core.Game;

// Time-based event queue. Owned by Game; scenarios schedule actions via the
// world.Events passthrough (world.Events.Add(offset, ...)) and SimObjects that
// need to schedule receive the instance via constructor injection. Game.Tick
// advances the scheduler (scaled by Game.EventTimeScale) before World.Tick;
// ScenarioSession.End clears it between scenarios.
public sealed class EventScheduler
{
    private readonly List<Entry> entries = new();
    private float elapsed;
    private float startsAt;
    private bool authoring;

    public float Elapsed => elapsed;

    // Moves the clock without firing; whatever falls due fires on the next Tick.
    public void Advance(float seconds) => elapsed += MathF.Max(0f, seconds);

    // A run that begins `startsAt` into its scenario's timeline (IScenario.StartsAt): what
    // FromRunStart schedules before that is skipped, t=0 setup aside, and the rest plays that much
    // sooner.
    public void BeginRun(float startsAt)
    {
        Clear();
        this.startsAt = MathF.Max(0f, startsAt);
    }

    // Add offsets inside `schedule` count from t=0 on the scenario's timeline, not now: an Ai
    // started late still means its times as run times. Entries already past fire on the next Tick.
    public T FromRunStart<T>(Func<T> schedule)
    {
        var now = elapsed;
        var wasAuthoring = authoring;
        elapsed = 0f;
        authoring = true;
        try
        {
            return schedule();
        }
        finally
        {
            elapsed = now;
            authoring = wasAuthoring;
        }
    }

    public void FromRunStart(Action schedule) => FromRunStart(() =>
    {
        schedule();
        return true;
    });

    // No more scheduled work. A scenario's whole timeline lives in this queue, so this
    // doubles as a generic "reached its declared end" signal for whoever's watching (Game
    // uses it to infer a clean run for the mechanic-streak counter) without needing scenarios
    // to report completion themselves.
    public bool IsEmpty => entries.Count == 0;

    // The caller's file:line rides along to the fire log, so a trace line leads back to the
    // timeline entry. Wrappers that schedule on their caller's behalf forward their own caller info.
    public void Add(float offset, Action action, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        var time = elapsed + MathF.Max(0f, offset);
        var source = $"{Path.GetFileName(file)}:{line}";
        if (authoring && startsAt > 0f && time > 0f)
        {
            if (time < startsAt)
            {
                Plugin.Log.Verbose($"[EventScheduler] skipped {source} (due {time:F2}, before the run's start at {startsAt:F2})");
                return;
            }
            time -= startsAt;
        }
        var index = entries.FindIndex(e => e.Time > time);
        var entry = new Entry(time, action, source);
        if (index < 0) entries.Add(entry);
        else entries.Insert(index, entry);
    }

    public void Tick(float deltaSeconds)
    {
        elapsed += deltaSeconds;
        while (entries.Count > 0 && entries[0].Time <= elapsed)
        {
            var due = entries[0];
            entries.RemoveAt(0);
            // An unhandled exception here previously took down every remaining
            // entry, not just this one: the exception unwinds straight out of
            // Tick, so the next call from a later frame resumes at whatever's
            // now first in the queue -- but if that one throws too (a scenario
            // bug that fires on every subsequent tick, e.g. stale replicated
            // state one specific scenario's Ai reads), the whole rest of the run
            // silently stops progressing, one discarded entry at a time, with
            // nothing but a log line to show for it. Isolating each entry means
            // one broken callback loses only itself.
            Plugin.Log.Verbose($"[EventScheduler] fired {due.Source} (due {due.Time:F2})");
            try
            {
                due.Action();
            }
            catch (Exception e)
            {
                DiagnosticLog.Warn($"[EventScheduler] Scheduled action at t={due.Time:F2} ({due.Source}) threw and was skipped: {e}");
            }
        }
    }

    public void Clear()
    {
        entries.Clear();
        elapsed = 0f;
        startsAt = 0f;
    }

    private readonly record struct Entry(float Time, Action Action, string Source);
}
