using AnoMech.Integrations.Iinact;

namespace AnoMech.Tests;

public class IinactBridgeTests
{
    private const float Frame = 1f / 60f;
    private const uint Instance = 0x800375D2;
    private const uint Top = 1122;
    private const uint Umad = 1363;
    private const uint Inn = 177;

    private sealed class FakeSink : INetLogSink
    {
        public List<NetLogEntry> Written { get; } = [];
        public bool KeepOffDisk { get; set; }
        public bool Refuses { get; set; }

        public void Write(NetLogEntry entry)
        {
            if (Refuses) throw new InvalidOperationException("refused");
            Written.Add(entry);
        }
    }

    private FakeSink sink = null!;
    private INetLogSink? found;
    private readonly List<TimeSpan> slept = [];
    private IinactBridge bridge = null!;

    [SetUp]
    public void SetUp()
    {
        sink = new FakeSink();
        found = sink;
        slept.Clear();
        bridge = new IinactBridge(() => (found, found is null ? "IINACT isn't loaded" : null), id => $"zone {id}", () => Instance, slept.Add);
    }

    [TearDown]
    public void TearDown() => bridge.Dispose();

    private static readonly NetLogEntry Cast = new(20, "cast");
    private static readonly NetLogEntry Marker = new(27, "marker");

    private void Tick(bool inStay = true, bool engaged = false, float seconds = Frame)
    {
        for (var t = 0f; t < seconds; t += Frame) bridge.Tick(Frame, inStay, engaged, Umad, Inn);
    }

    private void EnterTop() => bridge.Tick(Frame, true, false, Top, Top);

    private IEnumerable<string> Written => sink.Written.Select(e => $"{e.Type}|{e.Body}");

    [Test]
    public void AStayOpensOnTheSimsZoneAndClosesOnTheClients()
    {
        Tick();
        Tick(inStay: false);

        Assert.That(Written, Is.EqualTo(new[] { "1|553|zone 1363", "1|B1|zone 177" }));
    }

    [Test]
    public void TheStayIsKeptOffDiskUntilItsLinesHaveDrained()
    {
        Tick();
        Assert.That(sink.KeepOffDisk, Is.True);

        Tick(inStay: false);
        Assert.That(sink.KeepOffDisk, Is.True, "IINACT's queue still holds the stay's last lines");

        Tick(inStay: false, seconds: 3f);
        Assert.That(sink.KeepOffDisk, Is.False);
    }

    [Test]
    public void AQuickReentryKeepsTheSwitchItFirstFound()
    {
        Tick();
        Tick(inStay: false);
        Tick();
        Tick(inStay: false, seconds: 3f);

        Assert.That(sink.KeepOffDisk, Is.False);
    }

    [Test]
    public void WithoutIinactNothingIsWrittenAndTheSettingsSayWhy()
    {
        found = null;
        Tick();
        bridge.Write(Cast);

        Assert.That(bridge.Writing, Is.False);
        Assert.That(bridge.LastResult, Is.EqualTo("IINACT isn't loaded"));
    }

    [Test]
    public void ARunsLinesWaitUntilCactbotHasStartedThePull()
    {
        Tick();
        sink.Written.Clear();
        bridge.OnScenarioStarted(Umad);

        bridge.Write(Cast);
        Tick(engaged: false, seconds: 1f);
        Assert.That(sink.Written, Is.Empty, "combat hasn't started");

        Tick(engaged: true, seconds: 0.2f);
        Assert.That(sink.Written, Is.Empty, "cactbot may not have polled yet");

        Tick(engaged: true, seconds: 0.3f);
        bridge.Write(Marker);
        Assert.That(Written, Is.EqualTo(new[] { "20|cast", "27|marker" }));
    }

    [Test]
    public void ATopRunOpensWithTheFightsFirstHeadMarker()
    {
        EnterTop();
        sink.Written.Clear();
        bridge.OnScenarioStarted(Top);

        bridge.Write(Marker);
        Tick(engaged: true, seconds: 1f);

        Assert.That(Written, Is.EqualTo(new[] { "27|E0000000||0000|0000|0017|0000|0000|0000", "27|marker" }));
    }

    [Test]
    public void TheTeardownIsMutedAndTheWipeFollowsIt()
    {
        Tick();
        sink.Written.Clear();

        bridge.OnScenarioStopping();
        bridge.Write(Cast);
        bridge.OnScenarioStopped();
        bridge.Write(Marker);

        Assert.That(Written, Is.EqualTo(new[] { "33|800375D2|4000000F|00|00|00|00", "27|marker" }));
    }

    [Test]
    public void ARunStoppedBeforeCactbotStartedThePullDropsWhatItHeld()
    {
        Tick();
        sink.Written.Clear();
        bridge.OnScenarioStarted(Umad);
        bridge.Write(Cast);

        bridge.OnScenarioStopping();
        bridge.OnScenarioStopped();
        Tick(engaged: true, seconds: 1f);

        Assert.That(Written, Is.EqualTo(new[] { "33|800375D2|4000000F|00|00|00|00" }));
    }

    [Test]
    public void ASinkThatRefusesEndsTheStaysLines()
    {
        Tick();
        sink.Refuses = true;

        bridge.Write(Cast);

        Assert.That(bridge.Writing, Is.False);
        Assert.That(bridge.LastResult, Does.Contain("stopped taking lines"));
    }

    [Test]
    public void UnloadingMidStayWaitsForTheQueueBeforeRestoringTheSwitch()
    {
        Tick();

        bridge.StopWriting();
        bridge.Write(Cast);
        bridge.Dispose();

        Assert.That(Written, Has.None.EqualTo("20|cast"));
        Assert.That(slept, Has.Count.EqualTo(1));
        Assert.That(sink.KeepOffDisk, Is.False);
    }
}
