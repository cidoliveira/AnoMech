using System;
using System.Diagnostics;

namespace AnoMech.Integrations.Splatoon;

// Decides, frame by frame, what the InCombat condition should read while a scenario runs.
// A restart ends one encounter and begins the next on the same frame, but Splatoon and cactbot
// only start a pull afresh on the condition's rising edge: after a restart the flag has to read
// false for a while first, or the restart is invisible to them.
internal sealed class CombatGate
{
    // Splatoon polls the condition every frame, cactbot every 16 ms with the game focused and every
    // 300 ms without: longer than its slow poll.
    public const float GapSeconds = 0.4f;

    private readonly Func<double> clock;
    private bool pending;
    private float gapLeft;
    private double engagedEndedAt = double.NegativeInfinity;

    public CombatGate(Func<double>? clock = null) => this.clock = clock ?? (() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);

    public bool Engaged { get; private set; }

    // True from Begin until End: the caller holds the encounter conditions for this whole span.
    public bool Owns => pending || Engaged;

    // Waits out what is left of the gap since the last pull ended; from rest there is none left.
    public void Begin()
    {
        pending = true;
        Engaged = false;
        gapLeft = (float)Math.Max(0, GapSeconds - (clock() - engagedEndedAt));
    }

    // Returns whether the gate held the conditions, i.e. whether the caller must clear them.
    public bool End()
    {
        var owned = Owns;
        if (Engaged) engagedEndedAt = clock();
        pending = false;
        Engaged = false;
        gapLeft = 0f;
        return owned;
    }

    // The InCombat value to write this frame, or null to leave the condition alone.
    public bool? Tick(float deltaSeconds)
    {
        if (Engaged) return true;
        if (!pending) return null;
        if (gapLeft > 0f)
        {
            gapLeft -= deltaSeconds;
            return false;
        }
        pending = false;
        Engaged = true;
        return true;
    }
}
