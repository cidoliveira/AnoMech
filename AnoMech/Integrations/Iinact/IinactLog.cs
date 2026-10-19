using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

namespace AnoMech.Integrations.Iinact;

// IINACT's copy of FFXIV_ACT_Plugin's log writer, reached by reflection: IINACT has no IPC that
// writes lines. A line written here joins the queue IINACT's packet parsing fills, so its parser,
// OverlayPlugin and cactbot take it as they take a real one. Found again for each sim stay: a
// reload of IINACT gives it a new load context, and the old context's statics go empty.
internal sealed class IinactLog : INetLogSink
{
    // Dalamud names a plugin's load context after its assembly.
    private const string LoadContextName = "IINACT";
    private const string ActAssemblyName = "Advanced Combat Tracker";
    private const string ActGlobalsTypeName = "Advanced_Combat_Tracker.ActGlobals";

    private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private readonly object form;
    private readonly object logOutput;
    private readonly MethodInfo writeLine;
    private readonly Type messageType;

    private IinactLog(object form, object logOutput, MethodInfo writeLine)
    {
        this.form = form;
        this.logOutput = logOutput;
        this.writeLine = writeLine;
        messageType = writeLine.GetParameters()[0].ParameterType;
    }

    public static (INetLogSink? Sink, string? Missing) FindSink() => (Find(out var missing), missing);

    public static IinactLog? Find(out string? missing)
    {
        foreach (var context in AssemblyLoadContext.All)
        {
            if (context.Name != LoadContextName) continue;
            var globals = context.Assemblies.FirstOrDefault(a => a.GetName().Name == ActAssemblyName)?.GetType(ActGlobalsTypeName);
            // A context IINACT has left behind on a reload: its statics are cleared.
            if (globals?.GetField("oFormActMain", AnyStatic)?.GetValue(null) is not { } form) continue;
            return FromForm(form, out missing);
        }
        missing = "IINACT isn't loaded";
        return null;
    }

    // From NotACT's FormActMain: FfxivPlugin._dataCollection._logOutput, an ILogOutput whose
    // WriteLine(LogMessageType, DateTime, string) puts the type and timestamp in front of the
    // fields and the hash after them.
    internal static IinactLog? FromForm(object form, out string? missing)
    {
        var output = Get(form, "FfxivPlugin") is { } plugin && Get(plugin, "_dataCollection") is { } data ? Get(data, "_logOutput") : null;
        if (output is null)
        {
            missing = "IINACT's FFXIV parser isn't running yet";
            return null;
        }
        if (FindWriteLine(output.GetType()) is not { } writeLine)
        {
            missing = $"IINACT's log writer ({output.GetType().FullName}) has no WriteLine this version knows";
            return null;
        }
        missing = null;
        return new IinactLog(form, output, writeLine);
    }

    public void Write(NetLogEntry entry)
        => writeLine.Invoke(logOutput, [Enum.ToObject(messageType, entry.Type), DateTime.Now, entry.Body]);

    // IINACT's switch for keeping PvP off disk, which its log thread checks per line: on, nothing
    // reaches the log file, sim lines included.
    public bool KeepOffDisk
    {
        get => Get(form, "DisableWritingPvpLogFile") is true;
        set => Set(form, "DisableWritingPvpLogFile", value);
    }

    // The writer may implement WriteLine only explicitly, through its interface.
    private static MethodInfo? FindWriteLine(Type type)
        => type.GetMethods(AnyInstance).Concat(type.GetInterfaces().SelectMany(i => i.GetMethods())).FirstOrDefault(IsWriteLine);

    private static bool IsWriteLine(MethodInfo method)
        => method.Name.EndsWith("WriteLine", StringComparison.Ordinal)
           && method.GetParameters() is [{ ParameterType.IsEnum: true }, var time, var line]
           && time.ParameterType == typeof(DateTime) && line.ParameterType == typeof(string);

    private static object? Get(object target, string name)
    {
        for (var type = target.GetType(); type != null; type = type.BaseType)
        {
            if (type.GetField(name, AnyInstance | AnyStatic) is { } field) return field.GetValue(field.IsStatic ? null : target);
            if (type.GetProperty(name, AnyInstance | AnyStatic) is { CanRead: true } property)
                return property.GetValue(property.GetMethod!.IsStatic ? null : target);
        }
        return null;
    }

    private static void Set(object target, string name, object value)
    {
        for (var type = target.GetType(); type != null; type = type.BaseType)
        {
            if (type.GetField(name, AnyInstance | AnyStatic) is { } field)
            {
                field.SetValue(field.IsStatic ? null : target, value);
                return;
            }
            if (type.GetProperty(name, AnyInstance | AnyStatic) is { CanWrite: true } property)
            {
                property.SetValue(property.SetMethod!.IsStatic ? null : target, value);
                return;
            }
        }
        throw new MissingMemberException(target.GetType().FullName, name);
    }
}
