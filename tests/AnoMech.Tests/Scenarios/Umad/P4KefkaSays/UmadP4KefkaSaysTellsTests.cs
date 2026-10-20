using System.Globalization;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Scenarios.Umad.P4KefkaSays;
using StatusId = AnoMech.Scenarios.Umad.UmadConstants.StatusId;

namespace AnoMech.Tests;

// Kefka Says as a client-side reader takes it from what reaches the game (Splatoon's P4 reminder
// reads it this way): a teller, Neo Exdeath or Chaos, shows whether its coming cast lies by the
// loop VFX of KefkaLiesVfx's param, the last action a teller landed says whether the debuffs that
// follow lie, and the reader looks at statuses once a frame, so a debuff is read with the last action
// landed by the end of the frame it arrives in.
public class UmadP4KefkaSaysTellsTests
{
    private static readonly string[] WaveDebuffs = Ids(StatusId.AccelerationBomb, StatusId.ForkedLightning, StatusId.CompressedWater, StatusId.CursedShriek);
    private static readonly string[] ChaosDebuffs = Ids(StatusId.Entropy, StatusId.DynamicFluid);
    private static readonly string[] FinalDebuffs = Ids(StatusId.BeyondDeath, StatusId.AllaganField, StatusId.WhiteWound, StatusId.BlackWound);

    [Test]
    public void EveryDebuffIsReadWithItsTellersTruth(
        [Values] bool exdeath1Real, [Values] bool exdeath2Real, [Values] bool chaos1Real, [Values] bool chaos2Real, [Values] bool exdeath3Real)
    {
        var timeline = RunTo(53f, o =>
        {
            o.ExdeathCast1Real = exdeath1Real;
            o.ExdeathCast2 = exdeath2Real ? ExdeathCast2Mode.Real : ExdeathCast2Mode.Fake;
            o.ChaosCast1Real = chaos1Real;
            o.ChaosCast2Real = chaos2Real;
            o.ExdeathCast3Real = exdeath3Real;
        });

        bool Real(Debuff debuff) => debuff.Status switch
        {
            var s when WaveDebuffs.Contains(s) => debuff.Time < 30f ? exdeath1Real : exdeath2Real,
            var s when ChaosDebuffs.Contains(s) => debuff.Time < 35f ? chaos1Real : chaos2Real,
            _ => exdeath3Real,
        };
        var debuffs = Read(timeline);
        var misread = debuffs.Where(d => d.Lies != !Real(d))
            .GroupBy(d => (d.Time, d.Lies))
            .Select(g => $"{string.Join(" ", g.Select(d => d.Status).Distinct())} at {g.Key.Time:F2} read as "
                         + (g.Key.Lies is { } lies ? lies ? "a lie" : "true" : "nothing"));

        Assert.Multiple(() =>
        {
            Assert.That(debuffs.Select(d => d.Status), Is.SupersetOf(WaveDebuffs.Concat(ChaosDebuffs).Append(StatusId.BeyondDeath.ToString())));
            Assert.That(misread, Is.Empty);
        });
    }

    private sealed record Debuff(string Status, float Time, bool? Lies);

    private static List<Debuff> Read(SimTimeline timeline)
    {
        var tells = new Dictionary<string, bool>();
        bool? lies = null;
        var read = new List<Debuff>();
        foreach (var frame in timeline.Rows.GroupBy(row => row["t"]))
        {
            var arrived = new List<string>();
            foreach (var row in frame)
            {
                if (row["kind"] == "status_gain" && row["id"] == StatusId.KefkaLiesVfx.ToString())
                    tells[row["actor"]] = Param(row) is 1120 or 1122;
                else if (row["kind"] == "ability" && tells.TryGetValue(row["actor"], out var real))
                    lies = !real;
                else if (row["kind"] == "status_gain" && WaveDebuffs.Concat(ChaosDebuffs).Concat(FinalDebuffs).Contains(row["id"]))
                    arrived.Add(row["id"]);
            }
            var time = float.Parse(frame.Key, CultureInfo.InvariantCulture);
            read.AddRange(arrived.Select(status => new Debuff(status, time, lies)));
        }
        return read;
    }

    private static int Param(Dictionary<string, string> row)
        => int.Parse(row["detail"].Split(' ').Single(part => part.StartsWith("param="))["param=".Length..], CultureInfo.InvariantCulture);

    private static SimTimeline RunTo(float stopAt, Action<UmadP4KefkaSaysStateOverrides> truths)
    {
        SimTimeline? timeline = null;
        var run = ScenarioRun.Execute(typeof(UmadP4KefkaSaysScenario), 0, 1, new ScenarioRunOptions
        {
            Overrides = o =>
            {
                var overrides = (UmadP4KefkaSaysStateOverrides)o;
                UmadP4KefkaSaysScenarioTests.Pin(overrides);
                truths(overrides);
            },
            StopAt = stopAt,
            Probe = _ => timeline ??= ((FakeBattleCharas)Natives.BattleCharas).Timeline,
        });
        Assert.That(run.Passed, Is.True, run.ToString);
        return timeline!;
    }

    private static string[] Ids(params ushort[] statusIds) => statusIds.Select(id => id.ToString()).ToArray();
}
