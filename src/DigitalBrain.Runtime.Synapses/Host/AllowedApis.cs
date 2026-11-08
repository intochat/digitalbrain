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

    /// <summary>DigitalBrain namespaces a unit may reference (matched exactly), less <see cref="RefusedDigitalBrainTypes"/>.</summary>
    public static readonly string[] DigitalBrainNamespaces =
    [
        "DigitalBrain",
        "DigitalBrain.Rules",
        "DigitalBrain.Time",
        "DigitalBrain.UI",
        "DigitalBrain.Capabilities",
        "DigitalBrain.Capabilities.Compute",
        "DigitalBrain.Capabilities.Connections",
        "DigitalBrain.Capabilities.Http",
        "DigitalBrain.Capabilities.Identity",
        "DigitalBrain.Capabilities.Secrets",
        "DigitalBrain.Capabilities.Storage",
        "DigitalBrain.Capabilities.Tools",
    ];

    /// <summary>
    /// Types in <see cref="DigitalBrainNamespaces"/> a unit may not reference: the synapse runtime (the host, sources, installs,
    /// catalog, views), neuron plumbing and privileged module types. The synapse surface (attributes, Reaction, SignalEnvelope,
    /// Check, view nodes, signals) stays.
    /// </summary>
    public static readonly string[] RefusedDigitalBrainTypes =
    [
        "DigitalBrain.ISynapseHost", "DigitalBrain.ISourceStore", "DigitalBrain.ISources", "DigitalBrain.SourcesNeuron", "DigitalBrain.SourcesState",
        "DigitalBrain.SourceRecord", "DigitalBrain.LoadedSource", "DigitalBrain.NoSynapseRestorer",
        "DigitalBrain.IInstalls", "DigitalBrain.InstallsNeuron", "DigitalBrain.InstallsState", "DigitalBrain.InstalledGateIndex", "DigitalBrain.InstallRestore",
        "DigitalBrain.ISynapseCatalogSource", "DigitalBrain.SynapseCatalogSource", "DigitalBrain.SynapseCatalog", "DigitalBrain.UnboundSlotResolver",
        "DigitalBrain.IViewReader", "DigitalBrain.StateNeuron", "DigitalBrain.BddNeuron", "DigitalBrain.IBdd",
        "DigitalBrain.INeuronResolver", "DigitalBrain.IBrainSignalLog", "DigitalBrain.IPolicies", "DigitalBrain.PoliciesNeuron",
        "DigitalBrain.InProcessHostOptions", "DigitalBrain.ModuleAuth", "DigitalBrain.PrivilegedGrant", "DigitalBrain.Secret",
    ];

    /// <summary>Types refused although their namespace is open.</summary>
    public static readonly string[] RefusedTypes =
    [
        // Progress<T> posts to the captured SynchronizationContext or the thread pool.
        "System.Progress",
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
        // lock(obj) compiles to Monitor.Enter/Exit; every other Monitor member is refused below.
        "System.Threading.Monitor",
        "System.Threading.Lock",
        "System.Threading.Tasks.Sources.ManualResetValueTaskSourceCore",
        "System.Threading.Tasks.Sources.IValueTaskSource",
        "System.Threading.Tasks.Sources.ValueTaskSourceStatus",
        "System.Threading.Tasks.Sources.ValueTaskSourceOnCompletedFlags",
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
        "System.Runtime.CompilerServices.EnumeratorCancellationAttribute",
        "System.Runtime.CompilerServices.SwitchExpressionException",
        "System.Runtime.CompilerServices.RequiresLocationAttribute",
        "System.Runtime.CompilerServices.IsUnmanagedAttribute",
        "System.Runtime.CompilerServices.YieldAwaitable",
        "System.Runtime.CompilerServices.ConfiguredCancelableAsyncEnumerable",
        "System.Runtime.CompilerServices.PoolingAsyncValueTaskMethodBuilder",
        "System.Runtime.CompilerServices.InlineArray2",
        "System.Runtime.CompilerServices.InlineArray3",
        "System.Runtime.CompilerServices.DecimalConstantAttribute",
        // Inline arrays and span collection expressions compile to Unsafe.As/Add and MemoryMarshal.CreateSpan inside
        // <PrivateImplementationDetails>; every other member is refused below, and those four only pass from there (CompilerOnly).
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
        "System.Runtime.InteropServices.MemoryMarshal.*",
        "System.Threading.Monitor.*",
        "System.AppContext.*",
        "System.Buffers.ArrayPool.get_Shared",
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
        "System.Environment.get_CurrentManagedThreadId",
        "System.Threading.Monitor.Enter",
        "System.Threading.Monitor.Exit",
        "System.Runtime.CompilerServices.Unsafe.As",
        "System.Runtime.CompilerServices.Unsafe.Add",
        "System.Runtime.InteropServices.MemoryMarshal.CreateSpan",
        "System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan",
    ];

    /// <summary>Exempt members that pass only when every call site is in the compiler's &lt;PrivateImplementationDetails&gt; type.</summary>
    public static readonly string[] CompilerOnly =
    [
        "System.Runtime.CompilerServices.Unsafe.As",
        "System.Runtime.CompilerServices.Unsafe.Add",
        "System.Runtime.InteropServices.MemoryMarshal.CreateSpan",
        "System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan",
    ];

    /// <summary>True when the type is on the allowed surface.</summary>
    /// <param name="ns">The outermost type's namespace.</param>
    /// <param name="type">The full type name.</param>
    public static bool IsAllowedType(string ns, string type)
    {
        if (Within(type, RefusedTypes) || Within(type, RefusedDigitalBrainTypes))
        {
            return false;
        }

        return DigitalBrainNamespaces.Contains(ns, StringComparer.Ordinal) || Namespaces.Contains(ns, StringComparer.Ordinal) || Within(type, Types);
    }

    // The type is one of the names or nested in one.
    private static bool Within(string type, string[] names) =>
        names.Any(name => string.Equals(type, name, StringComparison.Ordinal) || type.StartsWith(name + ".", StringComparison.Ordinal));

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
