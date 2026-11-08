using AnoMech.Core.Game.Party;
using AnoMech.Integrations.Iinact;
using AnoMech.Scenarios.Umad.P4KefkaSays;

namespace AnoMech.Tests;

// What cactbot's Kefka Says triggers read from the log a run writes: every debuff call rests on the
// bosses' tells (808 with count 45F to 462, in the order they come), and some fire only on
// an action under its boss's name, though a helper casts it.
public class CactbotKefkaSaysTests
{
    [Test]
    public void TheTellsReachTheLogInOrderWithTheCountsCactbotMatches()
    {
        // Pinned: Exdeath 1 real, Chaos 1 real, Exdeath 2 lying, Chaos 2 real, Exdeath 3 and 4 real.
        var tells = Log().Where(line => line.StartsWith("26|808|")).Select(line => line.Split('|')[8]);

        Assert.That(tells, Is.EqualTo(new[] { "462", "460", "461", "460", "462", "462" }));
    }

    [Test]
    public void AHelpersAntilightIsLoggedUnderNeoExdeath()
    {
        var sources = Log().Where(line => line.StartsWith("21|") || line.StartsWith("22|"))
            .Select(line => line.Split('|'))
            .Where(fields => fields[3] is "C394" or "C395")
            .Select(fields => fields[2])
            .Distinct();

        Assert.That(sources, Is.EqualTo(new[] { "Neo Exdeath" }));
    }

    private static List<string> Log()
    {
        var lines = new List<string>();
        var run = ScenarioRun.Execute(typeof(UmadP4KefkaSaysScenario), 0, 1, new ScenarioRunOptions
        {
            PlayerRole = PartyRole.PhysRangedDps,
            Overrides = o => UmadP4KefkaSaysScenarioTests.Pin((UmadP4KefkaSaysStateOverrides)o),
            Configure = _ => NetLogTap.Install(entry => lines.Add($"{entry.Type}|{entry.Body}"), () => true, () => 0x800375D2),
            StopAt = 70f,
        });
        Assert.That(run.Failure, Is.Null, run.ToString);
        return lines;
    }
}
