using System.Collections.Generic;
using AnoMech.Integrations.BossMod;

namespace AnoMech.Tests;

// Stand-ins shaped like Boss Mod (properties) and Boss Mod Reborn (fields): the bridge only ever
// sees them through reflection, by member name.
public class BossModBridgeTests
{
    private const string ModuleType = "AnoMech.Tests.BossModBridgeTests+FightModule";

    public sealed class State
    {
        public uint ID;
        public State[]? NextStates;
    }

    public sealed class Phase(State initial)
    {
        public State InitialState = initial;
    }

    public sealed class PropertyStateMachine(List<Phase> phases)
    {
        public List<Phase> Phases { get; } = phases;
        public int ActivePhaseIndex { get; private set; } = -1;
        public State? ActiveState { get; private set; }
        public List<string> Log { get; } = [];

        public void Start() => TransitionToPhase(0);

        private void TransitionToPhase(int index)
        {
            ActivePhaseIndex = index;
            Log.Add($"phase {index}");
            TransitionToState(Phases[index].InitialState);
        }

        private void TransitionToState(State? state)
        {
            ActiveState = state;
            Log.Add($"state {state?.ID:X}");
        }
    }

    public sealed class FieldStateMachine(List<Phase> phases)
    {
        public List<Phase> Phases = phases;
        public int ActivePhaseIndex = -1;
        public State? ActiveState;
        public readonly List<string> Log = [];

        public void Start() => TransitionToPhase(0);

        private void TransitionToPhase(int index)
        {
            ActivePhaseIndex = index;
            Log.Add($"phase {index}");
            TransitionToState(Phases[index].InitialState);
        }

        private void TransitionToState(State? state)
        {
            ActiveState = state;
            Log.Add($"state {state?.ID:X}");
        }
    }

    public sealed class FightModule(object stateMachine)
    {
        public readonly object StateMachine = stateMachine;
    }

    public sealed class OtherModule
    {
        public readonly object StateMachine = new object();
    }

    public sealed class Manager(params object[] modules)
    {
        public List<object> LoadedModules { get; } = [.. modules];
    }

    private static List<Phase> BuildPhases()
    {
        State Chain(params uint[] ids)
        {
            State? next = null;
            for (var i = ids.Length - 1; i >= 0; i--)
                next = new State { ID = ids[i], NextStates = next == null ? null : [next] };
            return next!;
        }
        return
        [
            new(Chain(0x00000000, 0x00000001)),
            new(Chain(0x01000000)),
            new(Chain(0x02000000, 0x02030200, 0x02030210, 0x02040000)),
        ];
    }

    private static readonly BossModStart Start = new(ModuleType, 2, [0x02030200, 0x02030210]);

    [Test]
    public void MovesAPropertyStyleMachineThroughThePath()
    {
        var machine = new PropertyStateMachine(BuildPhases());
        machine.Start();

        Assert.That(BossModBridge.TrySync(new Manager(new OtherModule(), new FightModule(machine)), Start), Is.True);
        Assert.That(machine.ActivePhaseIndex, Is.EqualTo(2));
        Assert.That(machine.ActiveState!.ID, Is.EqualTo(0x02030210u));
        Assert.That(machine.Log[^4..], Is.EqualTo(new[] { "phase 2", "state 2000000", "state 2030200", "state 2030210" }));
    }

    [Test]
    public void MovesAFieldStyleMachineThroughThePath()
    {
        var machine = new FieldStateMachine(BuildPhases());
        machine.Start();

        Assert.That(BossModBridge.TrySync(new Manager(new FightModule(machine)), Start), Is.True);
        Assert.That(machine.ActivePhaseIndex, Is.EqualTo(2));
        Assert.That(machine.ActiveState!.ID, Is.EqualTo(0x02030210u));
    }

    [Test]
    public void WaitsUntilBossModStartsTheModule()
    {
        var machine = new PropertyStateMachine(BuildPhases());

        Assert.That(BossModBridge.TrySync(new Manager(new FightModule(machine)), Start), Is.False);
        Assert.That(machine.Log, Is.Empty);
    }

    [Test]
    public void WaitsWhileTheModuleIsNotLoaded()
    {
        Assert.That(BossModBridge.TrySync(new Manager(new OtherModule()), Start), Is.False);
    }

    [Test]
    public void AnUnknownStateLeavesTheMachineUntouched()
    {
        var machine = new PropertyStateMachine(BuildPhases());
        machine.Start();
        var missing = Start with { StatePath = [0x02030200, 0x0DEAD] };

        Assert.Throws<System.InvalidOperationException>(() => BossModBridge.TrySync(new Manager(new FightModule(machine)), missing));
        Assert.That(machine.ActivePhaseIndex, Is.EqualTo(0));
    }

    [Test]
    public void FindStateSurvivesCycles()
    {
        var a = new State { ID = 1 };
        var b = new State { ID = 2, NextStates = [a] };
        a.NextStates = [b];

        Assert.That(BossModBridge.FindState(a, 2), Is.SameAs(b));
        Assert.That(BossModBridge.FindState(a, 3), Is.Null);
    }
}
