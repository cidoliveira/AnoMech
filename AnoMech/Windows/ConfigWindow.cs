using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Integrations.Splatoon;

namespace AnoMech.Windows;

public class ConfigWindow : Window, IDisposable
{
    private readonly Configuration configuration;

    public ConfigWindow(Plugin plugin) : base("AnoMech Settings###AnoMechConfig")
    {
        Flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar |
                ImGuiWindowFlags.NoScrollWithMouse;

        Size = new Vector2(380, 400) * ImGuiHelpers.GlobalScale;
        SizeCondition = ImGuiCond.Always;

        configuration = Plugin.Config;
    }

    public void Dispose() { }

    public override void Draw()
    {
        var onInn = configuration.OpenSimMenuOnInn;
        if (ImGui.Checkbox("Open Sim Menu when entering Inn", ref onInn))
        {
            configuration.OpenSimMenuOnInn = onInn;
            configuration.Save();
        }

        var suppressBgm = configuration.SuppressBgm;
        if (ImGui.Checkbox("Suppress scenario BGM", ref suppressBgm))
        {
            configuration.SuppressBgm = suppressBgm;
            configuration.Save();
        }

        var resultMarks = configuration.EnableMechanicResultMarks;
        if (ImGui.Checkbox("Show mechanic success/failure marks", ref resultMarks))
        {
            configuration.EnableMechanicResultMarks = resultMarks;
            configuration.Save();
        }

        var userActions = configuration.EnableUserActions;
        if (ImGui.Checkbox("Resolve your own actions", ref userActions))
        {
            configuration.EnableUserActions = userActions;
            configuration.Save();
            if (userActions) Natives.UserActions.Enable();
            else Natives.UserActions.Disable();
        }

        if (configuration.EnableUserActions)
        {
            var threshold = configuration.CastInterruptThreshold;
            ImGui.SetNextItemWidth(90 * ImGuiHelpers.GlobalScale);
            if (ImGui.InputFloat("Slidecast window (s)", ref threshold, 0.05f, 0.1f, "%.2f"))
            {
                configuration.CastInterruptThreshold = Math.Clamp(threshold, 0f, 5f);
                configuration.Save();
            }

            var tankMitigation = configuration.EnableTankMitigation;
            if (ImGui.Checkbox("Require tank mitigation", ref tankMitigation))
            {
                configuration.EnableTankMitigation = tankMitigation;
                configuration.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Some tankbusters (depending on scenario support) will check your mitigation.");
        }

        ImGui.Separator();
        DrawPluginCompat();
        ImGui.Separator();

        var logging = configuration.EnableEventLogging;
        if (ImGui.Checkbox("Enable event logging", ref logging))
        {
            configuration.EnableEventLogging = logging;
            configuration.Save();
            if (logging) Plugin.LogManager.Open();
            else Plugin.LogManager.Close();
        }
        ImGui.SameLine();
        if (ImGui.Button("Open logs folder"))
            Plugin.LogManager.OpenLogsFolder();

#if DEBUG
        ImGui.Separator();

        var safeMode = configuration.SafeMode;
        if (ImGui.Checkbox("Safe mode (debug)", ref safeMode))
        {
            configuration.SafeMode = safeMode;
            configuration.Save();
        }
        if (safeMode)
            ImGui.TextWrapped(
                "Safe mode cuts you off from server traffic while in the sim zone. " +
                "You won't see players joining or leaving the party, ready checks, " +
                "or duty pops.");
        else
            ImGui.TextWrapped(
                "Safe mode off — server packets reach the engine, so you'll see " +
                "party updates, ready checks, and duty pops. It's easier to break " +
                "the sim zone this way. You still can't send anything to the server " +
                "while in the instance.");
#endif
    }

    private void DrawPluginCompat()
    {
        var compat = configuration.SplatoonCompat;
        if (ImGui.Checkbox("Plugin compatibility (Splatoon, BossMod...)", ref compat))
        {
            configuration.SplatoonCompat = compat;
            configuration.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(
                "While a scenario runs, sets the zone, flags you as in combat and in duty and sets\n" +
                "the fight's phase scene, so Splatoon layouts and scripts for the fight activate\n" +
                "and reset between pulls.");

        ImGui.SameLine();
        if (SplatoonCompat.IsSplatoonLoaded())
            ImGui.TextColored(new Vector4(0.4f, 0.9f, 0.4f, 1f), "Splatoon loaded");
        else
            ImGui.TextDisabled("Splatoon not loaded");

        if (!configuration.SplatoonCompat) return;

        var syncBossMod = configuration.SyncBossModModule;
        if (ImGui.Checkbox("Move BossMod's module to the scenario's start", ref syncBossMod))
        {
            configuration.SyncBossModModule = syncBossMod;
            configuration.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(
                "BossMod's fight modules follow the fight from the pull, so a scenario starting\n" +
                "mid-fight leaves them in an earlier phase with no hints (and nothing for NyaDraw's\n" +
                "BossMod AOEs). This moves the module to the scenario's mechanic when one is mapped.");
        if (Plugin.BossModBridge.LastResult is { } bossModResult)
            ImGui.TextDisabled($"BossMod: {bossModResult}");

        var sceneOverride = configuration.SplatoonSceneOverride;
        ImGui.SetNextItemWidth(90 * ImGuiHelpers.GlobalScale);
        if (ImGui.InputInt("Scene override", ref sceneOverride))
        {
            configuration.SplatoonSceneOverride = Math.Clamp(sceneOverride, SplatoonCompat.SceneOverrideOff, byte.MaxValue);
            configuration.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(
                "-1 uses each phase's own scene. Set a value to force Splatoon's Controller.Scene\n" +
                "for phases whose scene isn't known yet.");

        if (Plugin.SplatoonCompat.IsHoldingEncounter)
            ImGui.TextDisabled($"Active: in combat, scene {Plugin.SplatoonCompat.AppliedScene?.ToString() ?? "unchanged"}");
    }
}
