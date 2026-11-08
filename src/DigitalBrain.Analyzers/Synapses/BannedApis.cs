using System;
using System.Linq;

namespace DigitalBrain.Analyzers.Synapses;

/// <summary>
/// The APIs a synapse assembly may not touch (DB0045). The analyzer checks use sites; the synapse host checks the compiled
/// assembly's type and member references against the same list, so a build that skipped the analyzer is still refused.
/// Names are fully qualified with nested types joined by '.', and a member is "Type.Member" (constructors are ".ctor").
/// </summary>
internal static class BannedApis
{
    /// <summary>Namespaces (or a type and everything nested in it) a synapse may not reference.</summary>
    public static readonly string[] Namespaces =
    [
        "System.Net",
        "System.IO",
        "System.Diagnostics.Process",
        "System.Reflection.Emit",
        "System.Runtime.Loader",
    ];

    /// <summary>Types inside a banned namespace that stay allowed: in-memory streams carry no outside access.</summary>
    public static readonly string[] AllowedTypes = ["System.IO.MemoryStream", "System.IO.Stream"];

    /// <summary>Members a synapse may not call; a trailing '*' matches any member name with that prefix.</summary>
    public static readonly string[] Members =
    [
        "System.Threading.Tasks.Task.Run",
        "System.Threading.Thread..ctor",
        "System.Reflection.Assembly.Load*",
    ];

    /// <summary>True when the fully qualified type is banned as a whole.</summary>
    /// <param name="type">The fully qualified type name.</param>
    public static bool IsBannedType(string type)
    {
        if (AllowedTypes.Contains(type, StringComparer.Ordinal)) { return false; }
        return Namespaces.Any(banned => string.Equals(type, banned, StringComparison.Ordinal) || type.StartsWith(banned + ".", StringComparison.Ordinal));
    }

    /// <summary>True when the member of the fully qualified type is banned, either through its type or by name.</summary>
    /// <param name="type">The fully qualified type name.</param>
    /// <param name="member">The member's metadata name.</param>
    public static bool IsBannedMember(string type, string member)
    {
        if (IsBannedType(type)) { return true; }
        var full = type + "." + member;
        return Members.Any(banned => banned.EndsWith("*", StringComparison.Ordinal)
            ? full.StartsWith(banned.Substring(0, banned.Length - 1), StringComparison.Ordinal)
            : string.Equals(full, banned, StringComparison.Ordinal));
    }
}
