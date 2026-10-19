using AnoMech.Core.Game;

namespace AnoMech.Tests;

public class EventSchedulerTests
{
    [Test]
    public void ARunStartedPartwaySkipsWhatCameBeforeAndPlaysTheRestThatMuchSooner()
    {
        var events = new EventScheduler();
        var fired = new List<string>();
        events.BeginRun(startsAt: 100f);
        events.FromRunStart(() =>
        {
            events.Add(0f, () => fired.Add("setup"));
            events.Add(50f, () => fired.Add("skipped"));
            events.Add(103f, () => fired.Add("kept"));
        });

        events.Tick(2.9f);
        Assert.That(fired, Is.EqualTo(new[] { "setup" }));
        events.Tick(0.2f);
        Assert.That(fired, Is.EqualTo(new[] { "setup", "kept" }));
        Assert.That(events.IsEmpty);
    }

    [Test]
    public void AnEntryAddedDuringThePartwayRunCountsFromNow()
    {
        var events = new EventScheduler();
        var fired = new List<string>();
        events.BeginRun(startsAt: 100f);
        events.Tick(1f);

        events.Add(2f, () => fired.Add("later"));

        events.Tick(1.9f);
        Assert.That(fired, Is.Empty);
        events.Tick(0.2f);
        Assert.That(fired, Is.EqualTo(new[] { "later" }));
    }

    [Test]
    public void ALateReplayLandsOnTheHostsRunTimes()
    {
        var events = new EventScheduler();
        var fired = new List<string>();
        events.BeginRun(startsAt: 100f);
        events.Tick(5f);

        events.FromRunStart(() =>
        {
            events.Add(103f, () => fired.Add("already due"));
            events.Add(110f, () => fired.Add("at 10s"));
        });

        events.Tick(0.1f);
        Assert.That(fired, Is.EqualTo(new[] { "already due" }));
        events.Tick(4.95f);
        Assert.That(fired, Is.EqualTo(new[] { "already due", "at 10s" }));
    }

    [Test]
    public void TheEntryAtThePartwayStartPlaysFirstAndOneJustBeforeIsSkipped()
    {
        var events = new EventScheduler();
        var fired = new List<string>();
        events.BeginRun(startsAt: 100f);
        events.FromRunStart(() =>
        {
            events.Add(99.99f, () => fired.Add("just before"));
            events.Add(100f, () => fired.Add("at the start"));
        });

        events.Tick(0.01f);
        Assert.That(fired, Is.EqualTo(new[] { "at the start" }));
        Assert.That(events.IsEmpty);
    }

    [Test]
    public void ANestedScheduleKeepsTheOuterOneOnTheTimeline()
    {
        var events = new EventScheduler();
        var fired = new List<string>();
        events.BeginRun(startsAt: 100f);
        events.FromRunStart(() =>
        {
            events.FromRunStart(() => events.Add(101f, () => fired.Add("inner")));
            events.Add(102f, () => fired.Add("outer"));
        });
        events.Tick(2.1f);
        Assert.That(fired, Is.EqualTo(new[] { "inner", "outer" }));

        events.Add(1f, () => fired.Add("from now"));

        events.Tick(1.1f);
        Assert.That(fired, Is.EqualTo(new[] { "inner", "outer", "from now" }));
    }

    [Test]
    public void AScheduleThatThrowsStillEndsTheTimeline()
    {
        var events = new EventScheduler();
        var fired = new List<string>();
        events.BeginRun(startsAt: 100f);
        Assert.Throws<InvalidOperationException>(() => events.FromRunStart(() => throw new InvalidOperationException()));
        events.Tick(1f);

        events.Add(2f, () => fired.Add("from now"));

        events.Tick(2.1f);
        Assert.That(fired, Is.EqualTo(new[] { "from now" }));
    }

    [Test]
    public void ClearForgetsThePartwayStart()
    {
        var events = new EventScheduler();
        var fired = new List<string>();
        events.BeginRun(startsAt: 100f);
        events.Clear();

        events.FromRunStart(() => events.Add(50f, () => fired.Add("full run")));

        events.Tick(50.1f);
        Assert.That(fired, Is.EqualTo(new[] { "full run" }));
    }
}
