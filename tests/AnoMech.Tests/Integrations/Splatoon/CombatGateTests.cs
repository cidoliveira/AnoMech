using AnoMech.Integrations.Splatoon;

namespace AnoMech.Tests;

public class CombatGateTests
{
    private const float Frame = 1f / 144f;

    private double now;
    private CombatGate NewGate() => new(() => now);

    [SetUp]
    public void SetUp() => now = 100;

    [Test]
    public void IdleGateLeavesTheConditionAlone()
    {
        var gate = NewGate();
        Assert.That(gate.Tick(Frame), Is.Null);
        Assert.That(gate.Owns, Is.False);
    }

    // The condition already reads out of combat, so the pull starts at once.
    [Test]
    public void AFirstStartEngagesOnItsFirstFrame()
    {
        var gate = NewGate();
        gate.Begin();

        Assert.That(gate.Tick(Frame), Is.True);
        Assert.That(gate.Engaged, Is.True);
    }

    // However fast the frames come: the gap is time, so a poller on a timer sees it too.
    [TestCase(1f / 30f)]
    [TestCase(1f / 144f)]
    public void ARestartReadsOutOfCombatForTheGapThenEngages(float frame)
    {
        var gate = NewGate();
        gate.Begin();
        RunUntilEngaged(gate);
        gate.End();
        gate.Begin();

        var outOfCombat = 0f;
        for (var i = 0; i < 1000 && gate.Tick(frame) is false; i++)
        {
            Assert.That(gate.Engaged, Is.False);
            outOfCombat += frame;
        }

        Assert.That(outOfCombat, Is.GreaterThanOrEqualTo(CombatGate.GapSeconds).And.LessThan(CombatGate.GapSeconds + 2 * frame));
        Assert.That(gate.Engaged, Is.True);
        Assert.That(gate.Tick(frame), Is.True, "stays engaged and keeps re-asserting");
    }

    [Test]
    public void ARestartAfterAShortRestOnlyWaitsOutTheRestOfTheGap()
    {
        var gate = NewGate();
        gate.Begin();
        RunUntilEngaged(gate);
        gate.End();
        now += CombatGate.GapSeconds - 0.1;
        gate.Begin();

        var outOfCombat = 0f;
        for (var i = 0; i < 1000 && gate.Tick(Frame) is false; i++) outOfCombat += Frame;

        Assert.That(outOfCombat, Is.EqualTo(0.1f).Within(2 * Frame));
    }

    [Test]
    public void ARestartAfterALongRestNeedsNoGap()
    {
        var gate = NewGate();
        gate.Begin();
        RunUntilEngaged(gate);
        gate.End();
        now += 5;
        gate.Begin();

        Assert.That(gate.Tick(Frame), Is.True);
    }

    [Test]
    public void GateOwnsTheConditionsFromBeginOnward()
    {
        var gate = NewGate();
        gate.Begin();
        Assert.That(gate.Owns, Is.True);
    }

    [Test]
    public void EndReportsWhetherTheGateHeldTheConditions()
    {
        var gate = NewGate();
        Assert.That(gate.End(), Is.False, "nothing to clear when never begun");

        gate.Begin();
        Assert.That(gate.End(), Is.True, "clears even before engaging");

        gate.Begin();
        RunUntilEngaged(gate);
        Assert.That(gate.End(), Is.True);
        Assert.That(gate.Owns, Is.False);
        Assert.That(gate.Tick(Frame), Is.Null);
    }

    private static void RunUntilEngaged(CombatGate gate)
    {
        for (var i = 0; i < 1000 && !gate.Engaged; i++) gate.Tick(Frame);
        Assert.That(gate.Engaged, Is.True);
    }
}
