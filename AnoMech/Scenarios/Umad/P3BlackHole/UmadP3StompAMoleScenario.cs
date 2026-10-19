using System.Collections.Generic;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Geometry;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Integrations.BossMod;
using AnoMech.Multiplayer;

namespace AnoMech.Scenarios.Umad.P3BlackHole;

// Black Hole's own timeline from the end of its last wave, so Stomp-a-Mole can be practised
// without playing the 137 seconds before it. Settings and strats are Black Hole's.
public sealed class UmadP3StompAMoleScenario : IMultiplayerReplayable
{
    public const float StartsAtSeconds = UmadP3BlackHoleScenario.StompAMoleStartsAt;

    private readonly UmadP3BlackHoleScenario blackHole = new(fromStompAMole: true);

    public string Name => "Stomp-a-Mole";
    public IPhase Phase => blackHole.Phase;
    public float StartsAt => StartsAtSeconds;
    public float BgmSecondsAtStart => StartsAtSeconds;

    // Boss Mod's UMADStates P3StompAMole from its first state, which waits for Exdeath's Blizzard
    // III cast and activates the stomp, Knock Down, Big Bang and freeze components.
    // UNVERIFIED in game; state id from Boss Mod's source.
    public BossModStart BossModStart { get; } =
        new("BossMod.Dawntrail.Ultimate.UMAD.UMAD", Phase: 2, StatePath: [0x02070000]);

    public bool SupportsMultiplayer => blackHole.SupportsMultiplayer;
    public IReadOnlyList<IScenarioAi> AiStrats => blackHole.AiStrats;

    public void Run(SimWorld world, int? selectedAi) => blackHole.Run(world, selectedAi);
    public void Tick(float delta, float elapsed) => blackHole.Tick(delta, elapsed);
    public void RunInstanceEvents(SimWorld world) => blackHole.RunInstanceEvents(world);

    public void DrawSettings() => blackHole.DrawSettings();
    public bool HasPerPlayerSettings => blackHole.HasPerPlayerSettings;
    public void DrawPerPlayerSettings() => blackHole.DrawPerPlayerSettings();
    public void DrawMultiplayerSettings() => blackHole.DrawMultiplayerSettings();
    public object SettingsOverrides => blackHole.SettingsOverrides;
    public IReadOnlyList<string> SettingsConflicts => blackHole.SettingsConflicts;

    public MpMessage? BuildReplayStateMessage() => blackHole.BuildReplayStateMessage();

    public object? StartReplay(MpMessage message, int aiIndex, PartyRole myRole, SimWorld replayWorld)
        => blackHole.StartReplay(message, aiIndex, myRole, replayWorld);

    public void RefreshLiveHandles(object shadowStateObj, IReadOnlyDictionary<int, SimEnemy> peerEnemies)
        => blackHole.RefreshLiveHandles(shadowStateObj, peerEnemies);

    public void RebuildPeerObstacles(ObstacleField obstacles, IReadOnlyDictionary<int, SimEnemy> peerEnemies,
        IReadOnlyDictionary<int, SimEventObject> peerEventObjects, SimCharacter? localPlayer)
        => blackHole.RebuildPeerObstacles(obstacles, peerEnemies, peerEventObjects, localPlayer);
}
