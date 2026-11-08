using System;
using System.Linq;

namespace DigitalBrain.Analyzers.Synapses;

/// <summary>
/// The APIs DB0045 reports at authoring time: deny by namespace or type, with a short allow-list. This list is advisory, for
/// messages at the use site; the boundary is the synapse host's allow-list (AllowedApis in DigitalBrain.Runtime.Synapses),
/// which refuses any build referencing anything off it. Names are fully qualified with nested types joined by '.', generic arity
/// dropped, and a member is "Type.Member" (constructors are ".ctor"; property accessors may be given by property name).
/// </summary>
internal static class BannedApis
{
    /// <summary>Namespaces, or types with everything nested in them, a synapse may not reference.</summary>
    public static readonly string[] Namespaces =
    [
        // Outside access: network, disk, processes, environment, console.
        "System.Net",
        "System.IO",
        "System.Diagnostics.Process",
        "System.Environment",
        "System.Console",
        "System.AppContext",
        "System.Progress",
        // Code loading and reflection (Type.GetType + Activator + Invoke reaches anything), and the dynamic binder.
        "System.Reflection",
        "System.Runtime.Loader",
        "System.Activator",
        "System.AppDomain",
        "Microsoft.CSharp.RuntimeBinder",
        // Native code.
        "System.Runtime.InteropServices",
        "System.Runtime.CompilerServices.Unsafe",
        "System.Security.UnverifiableCodeAttribute",
        "System.Security.SuppressUnmanagedCodeSecurityAttribute",
        // The thread pool and threads of its own.
        "System.Threading.Thread",
        "System.Threading.ThreadPool",
        "System.Threading.ThreadPoolBoundHandle",
        "System.Threading.Timer",
        "System.Timers",
        "System.Threading.Tasks.Parallel",
        "System.Threading.Tasks.TaskFactory",
        "System.Threading.Tasks.TaskScheduler",
        // Expression trees compile and invoke members by name.
        "System.Linq.Expressions",
    ];

    /// <summary>Types inside a banned namespace that stay allowed.</summary>
    public static readonly string[] AllowedTypes =
    [
        // In-memory streams carry no outside access.
        "System.IO.MemoryStream",
        "System.IO.Stream",
        // The compiler emits it for 'in' and 'ref readonly' parameters.
        "System.Runtime.InteropServices.InAttribute",
    ];

    /// <summary>Members of a banned type that stay allowed.</summary>
    public static readonly string[] AllowedMembers =
    [
        "System.Environment.NewLine",
        "System.Environment.TickCount64",
    ];

    /// <summary>Members of allowed types a synapse may not call; a trailing '*' matches any member name with that prefix.</summary>
    public static readonly string[] Members =
    [
        "System.Threading.Tasks.Task.Run",
        "System.Threading.Tasks.Task.Factory",
        "System.Threading.Tasks.Task.Start",
        "System.Threading.Tasks.Task.ContinueWith",
        "System.Delegate.DynamicInvoke",
        "System.Buffers.ArrayPool.Shared",
        "System.Type.GetType*",
        "System.Type.InvokeMember",
        "System.Delegate.CreateDelegate",
    ];

    // typeof(T) compiles to Type.GetTypeFromHandle, which records and nameof-free code still need.
    private static readonly string[] AllowedCalls = ["System.Type.GetTypeFromHandle"];

    /// <summary>True when the fully qualified type is banned as a whole.</summary>
    /// <param name="type">The fully qualified type name.</param>
    public static bool IsBannedType(string type)
    {
        if (AllowedTypes.Contains(type, StringComparer.Ordinal)) { return false; }

        // Attribute types in System.Reflection (assembly metadata, DefaultMember for indexers) describe code; they do nothing.
        if (type.StartsWith("System.Reflection.", StringComparison.Ordinal) && type.EndsWith("Attribute", StringComparison.Ordinal)) { return false; }

        return Namespaces.Any(banned => string.Equals(type, banned, StringComparison.Ordinal) || type.StartsWith(banned + ".", StringComparison.Ordinal));
    }

    /// <summary>True when merely naming the type is banned: a banned type with no allowed members (Environment is checked per member).</summary>
    /// <param name="type">The fully qualified type name.</param>
    public static bool IsBannedReference(string type) =>
        IsBannedType(type) && !AllowedMembers.Any(allowed => allowed.StartsWith(type + ".", StringComparison.Ordinal));

    /// <summary>True when the member of the fully qualified type is banned, either through its type or by name.</summary>
    /// <param name="type">The fully qualified type name.</param>
    /// <param name="member">The member's name (metadata or source).</param>
    public static bool IsBannedMember(string type, string member)
    {
        var property = member.StartsWith("get_", StringComparison.Ordinal) || member.StartsWith("set_", StringComparison.Ordinal) ? member.Substring(4) : member;
        if (AllowedMembers.Contains(type + "." + property, StringComparer.Ordinal)) { return false; }
        if (IsBannedType(type)) { return true; }

        var full = type + "." + property;
        if (AllowedCalls.Contains(type + "." + member, StringComparer.Ordinal)) { return false; }
        return Members.Any(banned => banned.EndsWith("*", StringComparison.Ordinal)
            ? full.StartsWith(banned.Substring(0, banned.Length - 1), StringComparison.Ordinal)
            : string.Equals(full, banned, StringComparison.Ordinal));
    }
}
