using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Dalamud.Plugin;

namespace AnoMech.Integrations.BossMod;

// Reaches the BossModuleManager of a loaded BossMod or BossMod Reborn. Neither exposes its
// modules over IPC, so this reads Dalamud's plugin list and the plugin's own fields. Every step
// can break when either plugin changes its internals; callers treat a null as "not available".
internal static class BossModReflection
{
    private const BindingFlags AnyInstance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    // Reborn keeps the manager on the plugin; Boss Mod builds it in TickService from its DI host.
    public static readonly string[] InternalNames = ["BossMod", "BossModReborn"];
    private const string ManagerField = "_bossmod";
    private const string TickServiceType = "BossMod.Services.TickService";

    // What the last FindManagers saw, for the give-up message: each loaded plugin and whether its
    // manager resolved.
    public static string LastScan { get; private set; } = "not scanned";
    private static readonly HashSet<string> WarnedPlugins = [];

    public static IEnumerable<(string Plugin, object Manager)> FindManagers()
    {
        var scan = new List<string>();
        foreach (var (plugin, manager) in Scan(scan))
            yield return (plugin, manager);
        LastScan = scan.Count == 0 ? "neither BossMod nor BossMod Reborn is loaded" : string.Join(", ", scan);
    }

    private static IEnumerable<(string Plugin, object Manager)> Scan(List<string> scan)
    {
        foreach (var (name, plugin) in LoadedPlugins())
        {
            object? manager = null;
            try
            {
                manager = ReadField(plugin, ManagerField) ?? FromTickService(plugin);
            }
            catch (Exception e)
            {
                // Scanned every frame while a start is pending.
                if (WarnedPlugins.Add(name))
                    Core.DiagnosticLog.Warn($"[BossModBridge] {name}: reading its module manager threw: {e.Message}");
            }
            scan.Add($"{name} ({(manager != null ? "module manager found" : "module manager not found")})");
            if (manager != null) yield return (name, manager);
        }
    }

    public static object? Read(object target, string member)
    {
        var type = target.GetType();
        if (type.GetProperty(member, AnyInstance) is { } property) return property.GetValue(target);
        return ReadField(target, member);
    }

    public static void Invoke(object target, string method, params object?[] args)
    {
        var info = target.GetType().GetMethod(method, AnyInstance)
                   ?? throw new MissingMethodException(target.GetType().FullName, method);
        info.Invoke(target, args);
    }

    private static object? ReadField(object target, string name)
    {
        for (var type = target.GetType(); type != null; type = type.BaseType)
            if (type.GetField(name, AnyInstance) is { } field) return field.GetValue(target);
        return null;
    }

    private static object? FromTickService(object plugin)
    {
        var tickType = plugin.GetType().Assembly.GetType(TickServiceType);
        if (tickType == null || FindServiceProvider(plugin) is not { } services) return null;
        return services.GetService(tickType) is { } tick ? ReadField(tick, ManagerField) : null;
    }

    // The hosting base class holds its host privately; the host exposes Services.
    private static IServiceProvider? FindServiceProvider(object plugin)
    {
        for (var type = plugin.GetType(); type != null; type = type.BaseType)
        {
            foreach (var field in type.GetFields(AnyInstance | BindingFlags.DeclaredOnly))
            {
                var value = field.GetValue(plugin);
                if (value is IServiceProvider provider) return provider;
                if (value?.GetType().GetProperty("Services", AnyInstance)?.GetValue(value) is IServiceProvider services)
                    return services;
            }
        }
        return null;
    }

    private static IEnumerable<(string Name, object Plugin)> LoadedPlugins()
    {
        IEnumerable? installed;
        try
        {
            var dalamud = typeof(IDalamudPluginInterface).Assembly;
            var managerType = dalamud.GetType("Dalamud.Plugin.Internal.PluginManager", true)!;
            var service = dalamud.GetType("Dalamud.Service`1", true)!.MakeGenericType(managerType);
            var pluginManager = service.GetMethod("Get", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null)!;
            installed = managerType.GetProperty("InstalledPlugins")!.GetValue(pluginManager) as IEnumerable;
        }
        catch (Exception e)
        {
            Core.DiagnosticLog.Warn($"[BossModBridge] Dalamud's plugin list is unreadable: {e.Message}");
            yield break;
        }
        if (installed == null) yield break;

        foreach (var local in installed)
        {
            var name = local.GetType().GetProperty("InternalName")?.GetValue(local) as string;
            if (name == null || Array.IndexOf(InternalNames, name) < 0) continue;
            if (local.GetType().GetProperty("IsLoaded")?.GetValue(local) is not true) continue;
            if (ReadField(local, "instance") is { } instance) yield return (name, instance);
        }
    }
}
