namespace AnoMech.Integrations.BossMod;

// Where a scenario begins in BossMod's module for the fight. BossMod modules are state machines
// that only advance from the pull, so a scenario starting mid-fight leaves them waiting in an
// earlier phase. Phase is the module's phase index; StatePath is entered in order, so a state
// whose Enter activates the components the practiced part needs can be passed through on the
// way to the state that waits for the scenario's first event.
public sealed record BossModStart(string ModuleType, int Phase, uint[] StatePath);
