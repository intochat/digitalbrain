namespace DigitalBrain.Runtime.Synapses;

/// <summary>
/// The surface a synapse build may reference (the host's allow-list). Names are fully qualified, nested types joined by '.',
/// generic arity dropped; members are metadata names (constructors ".ctor", property accessors "get_X").
/// </summary>
internal static class AllowedApis
{
    /// <summary>Namespaces whose every type is allowed (members still pass <see cref="RefusedMembers"/>). Matched exactly.</summary>
    public static readonly string[] Namespaces =
    [
        "System",
        "System.Collections",
        "System.Collections.Generic",
        "System.Collections.Immutable",
        "System.Collections.Frozen",
        "System.Linq",
        "System.Text",
        "System.Text.Json",
        "System.Text.Json.Serialization",
        "System.Text.Json.Nodes",
        "System.Text.RegularExpressions",
        "System.Globalization",
        "System.Numerics",
        "System.Buffers",
    ];

    /// <summary>Single types allowed inside namespaces that are otherwise closed (with their nested types).</summary>
    public static readonly string[] Types =
    [
        "System.Threading.Tasks.Task",
        "System.Threading.Tasks.ValueTask",
        "System.Threading.CancellationToken",
        "System.Threading.CancellationTokenSource",
        "System.Threading.Interlocked",
        "System.Threading.Volatile",
        "System.IO.MemoryStream",
        "System.IO.Stream",
        "System.IO.TextWriter",
        "System.IO.StringWriter",
        "System.IO.StringReader",
        "System.Runtime.InteropServices.InAttribute",
        "System.Runtime.InteropServices.MemoryMarshal",
        "System.Runtime.InteropServices.CollectionsMarshal",

        // What the compiler emits for records, nullable, required, init, collection expressions, inline arrays,
        // interpolated strings, async and iterators (read from a probe unit's TypeRefs).
        "System.Runtime.CompilerServices.CompilerGeneratedAttribute",
        "System.Runtime.CompilerServices.IsExternalInit",
        "System.Runtime.CompilerServices.IsReadOnlyAttribute",
        "System.Runtime.CompilerServices.IsByRefLikeAttribute",
        "System.Runtime.CompilerServices.NullableAttribute",
        "System.Runtime.CompilerServices.NullableContextAttribute",
        "System.Runtime.CompilerServices.RefSafetyRulesAttribute",
        "System.Runtime.CompilerServices.RequiredMemberAttribute",
        "System.Runtime.CompilerServices.CompilerFeatureRequiredAttribute",
        "System.Runtime.CompilerServices.CollectionBuilderAttribute",
        "System.Runtime.CompilerServices.InlineArrayAttribute",
        "System.Runtime.CompilerServices.ExtensionAttribute",
        "System.Runtime.CompilerServices.TupleElementNamesAttribute",
        "System.Runtime.CompilerServices.CompilationRelaxationsAttribute",
        "System.Runtime.CompilerServices.RuntimeCompatibilityAttribute",
        "System.Runtime.CompilerServices.DefaultInterpolatedStringHandler",
        "System.Runtime.CompilerServices.FormattableStringFactory",
        "System.Runtime.CompilerServices.RuntimeHelpers",
        "System.Runtime.CompilerServices.IAsyncStateMachine",
        "System.Runtime.CompilerServices.AsyncStateMachineAttribute",
        "System.Runtime.CompilerServices.IteratorStateMachineAttribute",
        "System.Runtime.CompilerServices.AsyncIteratorStateMachineAttribute",
        "System.Runtime.CompilerServices.AsyncTaskMethodBuilder",
        "System.Runtime.CompilerServices.AsyncValueTaskMethodBuilder",
        "System.Runtime.CompilerServices.AsyncVoidMethodBuilder",
        "System.Runtime.CompilerServices.AsyncIteratorMethodBuilder",
        "System.Runtime.CompilerServices.TaskAwaiter",
        "System.Runtime.CompilerServices.ValueTaskAwaiter",
        "System.Runtime.CompilerServices.ConfiguredTaskAwaitable",
        "System.Runtime.CompilerServices.ConfiguredValueTaskAwaitable",
        "System.Runtime.CompilerServices.ScopedRefAttribute",
        "System.Runtime.CompilerServices.ParamCollectionAttribute",
        "System.Runtime.CompilerServices.PreserveBaseOverridesAttribute",
        "System.Runtime.CompilerServices.DecimalConstantAttribute",
        // Inline arrays and span collection expressions compile to Unsafe.As/Add; every other member is refused below.
        "System.Runtime.CompilerServices.Unsafe",

        // Emitted on every Debug build (assembly attribute, DebuggerBrowsable on record members); they describe code only.
        "System.Diagnostics.DebuggableAttribute",
        "System.Diagnostics.DebuggerBrowsableAttribute",
        "System.Diagnostics.DebuggerBrowsableState",
        "System.Diagnostics.DebuggerHiddenAttribute",
        "System.Diagnostics.DebuggerStepThroughAttribute",
        "System.Diagnostics.CodeAnalysis.SetsRequiredMembersAttribute",
        "System.Diagnostics.CodeAnalysis.UnscopedRefAttribute",
        "System.Reflection.DefaultMemberAttribute",
    ];

    /// <summary>Members refused on allowed types; a trailing '*' matches any member name with that prefix.</summary>
    public static readonly string[] RefusedMembers =
    [
        "System.Type.GetType*",
        "System.Type.InvokeMember",
        "System.Activator.*",
        "System.Delegate.CreateDelegate",
        "System.Delegate.DynamicInvoke",
        "System.AppDomain.*",
        "System.Environment.*",
        "System.Console.*",
        "System.GC.*",
        "System.Threading.Tasks.Task.Run",
        "System.Threading.Tasks.Task.get_Factory",
        "System.Threading.Tasks.Task.Start",
        "System.Threading.Tasks.Task.RunSynchronously",
        "System.Threading.Tasks.Task.ContinueWith",
        "System.Threading.Tasks.Task..ctor",
        "System.Threading.Tasks.ValueTask.Run",
        "System.Runtime.CompilerServices.Unsafe.*",
        "System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject",
        "System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor",
        "System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor",
        "System.Runtime.CompilerServices.RuntimeHelpers.PrepareMethod",
        "System.Runtime.CompilerServices.RuntimeHelpers.PrepareDelegate",
        "System.Runtime.CompilerServices.RuntimeHelpers.AllocateTypeAssociatedMemory",
        "System.Runtime.CompilerServices.RuntimeHelpers.GetObjectValue",
    ];

    /// <summary>Members exempt from <see cref="RefusedMembers"/>.</summary>
    public static readonly string[] ExemptMembers =
    [
        // typeof(T) compiles to Type.GetTypeFromHandle.
        "System.Type.GetTypeFromHandle",
        "System.Environment.get_NewLine",
        "System.Environment.get_TickCount64",
        "System.Runtime.CompilerServices.Unsafe.As",
        "System.Runtime.CompilerServices.Unsafe.Add",
    ];

    /// <summary>True when the type is on the allowed surface.</summary>
    /// <param name="ns">The outermost type's namespace.</param>
    /// <param name="type">The full type name.</param>
    public static bool IsAllowedType(string ns, string type) =>
        ns == "DigitalBrain" || ns.StartsWith("DigitalBrain.", StringComparison.Ordinal)
        || Namespaces.Contains(ns, StringComparer.Ordinal)
        || Types.Any(allowed => string.Equals(type, allowed, StringComparison.Ordinal) || type.StartsWith(allowed + ".", StringComparison.Ordinal));

    /// <summary>True when the member's type is allowed and the member is not refused.</summary>
    /// <param name="ns">The outermost type's namespace.</param>
    /// <param name="type">The full type name.</param>
    /// <param name="member">The member's metadata name.</param>
    public static bool IsAllowedMember(string ns, string type, string member)
    {
        if (!IsAllowedType(ns, type))
        {
            return false;
        }

        var full = type + "." + member;
        if (ExemptMembers.Contains(full, StringComparer.Ordinal))
        {
            return true;
        }

        return !RefusedMembers.Any(refused => refused.EndsWith('*')
            ? full.StartsWith(refused[..^1], StringComparison.Ordinal)
            : string.Equals(full, refused, StringComparison.Ordinal));
    }
}
