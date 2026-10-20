using System;
using System.Collections.Generic;
using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.SimObjects;

namespace AnoMech.Core.EnemyActions;

// One SimEnemy.Cast(EnemyAction, ...), for a later timeline event to read what it did, or to hang
// what follows its ActionEffect on (AfterRelease). Filled at resolve; an action with no effects
// never resolves.
public sealed class EnemyActionCast
{
    private readonly List<Action> afterRelease = [];

    public bool IsResolved { get; private set; }
    public bool IsReleased { get; private set; }
    public bool IsCancelled { get; private set; }
    public Placement Origin { get; private set; }

    // At = where Who stood at resolve. Killed is decided at resolve; they die TimingSpec.DamageDelay later.
    public sealed record Hit(SimCharacter Who, Vector3 At, bool Killed);

    // Nearest to Origin first.
    public IReadOnlyList<Hit> Hits { get; private set; } = [];

    // Runs in the tick the ActionEffect goes out, right behind it, so what `run` sends reaches the
    // client after the action and never a frame before it; at once if it already went out. Never
    // for a cancelled cast.
    public void AfterRelease(Action run)
    {
        if (IsReleased) run();
        else if (!IsCancelled) afterRelease.Add(run);
    }

    internal void Cancelled() => IsCancelled = true;

    internal void Resolve(Placement origin, IReadOnlyList<Hit> hits)
    {
        IsResolved = true;
        Origin = origin;
        Hits = hits;
    }

    internal void Released()
    {
        IsReleased = true;
        var pending = afterRelease.ToArray();
        afterRelease.Clear();
        foreach (var run in pending) run();
    }
}
