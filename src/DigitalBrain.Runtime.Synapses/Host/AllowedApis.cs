namespace DigitalBrain.Runtime.Synapses;

/// <summary>
/// The surface a synapse build may reference (the host's allow-list). Names are fully qualified, nested types joined by '.',
/// generic arity dropped; members are metadata names (constructors ".ctor", property accessors "get_X").
/// <para>
/// The principle: allow members, never types with member deny-lists. A pure data type (string, int, collections) is allowed whole;
/// every other type that stays allowed (<see cref="MemberAllowList"/>: RuntimeHelpers, Type, Task, ValueTask, the awaiters and
/// method builders, CancellationToken(Source)) is allowed member by member, so a member a later .NET adds is refused until ruled on.
/// A deny-list inside an allowed type fails open (RuntimeHelpers.Box forged object references).
/// </para>
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

    /// <summary>
    /// The published language: the DigitalBrain namespaces written for unit authors, matched exactly. Every public type declared in
    /// one of them is open to a unit (members still pass <see cref="RefusedMembers"/>); every other DigitalBrain namespace is closed.
    /// There is no list of types: AuthoringSurfaceFacts proves that nothing declared here hands a unit a closed type.
    /// </summary>
    public static readonly string[] AuthoringNamespaces =
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
        // lock on a Lock field compiles to Lock.EnterScope; System.Threading.Monitor (lock on a plain object) is refused.
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
        "System.Runtime.CompilerServices.ScopedRefAttribute",
        "System.Runtime.CompilerServices.ParamCollectionAttribute",
        "System.Runtime.CompilerServices.PreserveBaseOverridesAttribute",
        "System.Runtime.CompilerServices.EnumeratorCancellationAttribute",
        "System.Runtime.CompilerServices.SwitchExpressionException",
        "System.Runtime.CompilerServices.RequiresLocationAttribute",
        "System.Runtime.CompilerServices.IsUnmanagedAttribute",
        "System.Runtime.CompilerServices.ConfiguredCancelableAsyncEnumerable",
        "System.Runtime.CompilerServices.PoolingAsyncValueTaskMethodBuilder",
        "System.Runtime.CompilerServices.InlineArray2",
        "System.Runtime.CompilerServices.InlineArray3",
        "System.Runtime.CompilerServices.DecimalConstantAttribute",
        // await foreach and await in a finally rethrow through it (compiler-emitted; Capture and Throw only, below).
        "System.Runtime.ExceptionServices.ExceptionDispatchInfo",
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
        // A resolver hands out JsonTypeInfo for any type; the Type-taking serializer overloads are refused by signature in AllowedReferences.
        "System.Text.Json.JsonSerializerOptions.get_TypeInfoResolver*",
        "System.Text.Json.JsonSerializerOptions.set_TypeInfoResolver*",
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
        "System.AppContext.*",
        "System.Buffers.ArrayPool.get_Shared",
    ];

    // The awaiter and builder members the compiler emits for await, async methods and async iterators.
    private static readonly string[] Awaiter = ["GetResult", "get_IsCompleted", "OnCompleted", "UnsafeOnCompleted"];
    private static readonly string[] Builder =
        ["Create", "Start", "SetResult", "SetException", "SetStateMachine", "get_Task", "AwaitOnCompleted", "AwaitUnsafeOnCompleted", "MoveNext", "Complete"];

    /// <summary>
    /// Types allowed member by member: only the listed members pass; every other member, and any a later .NET adds, is refused. An
    /// entry "Name(T1,T2)" allows only the overload with exactly those parameter types; a plain "Name" allows every overload.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> MemberAllowList = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        // Only what the compiler emits (array initialisers, span literals, array ranges, record PrintMembers); Box and the rest are refused.
        ["System.Runtime.CompilerServices.RuntimeHelpers"] = ["InitializeArray", "CreateSpan", "GetSubArray", "EnsureSufficientExecutionStack"],
        // What typeof, records' EqualityContract and type comparisons need: no TypeHandle, GetType, members, attributes or generics.
        ["System.Type"] =
        [
            "GetTypeFromHandle", "op_Equality", "op_Inequality", "get_Name", "get_FullName", "get_Namespace", "ToString", "Equals", "GetHashCode",
            "IsAssignableFrom", "IsAssignableTo", "get_IsValueType", "get_IsEnum", "get_IsGenericType",
        ],
        // No Run, Factory, Start, ContinueWith, ConfigureAwait, Yield, Wait or constructors: a unit's code stays on the activation's turn.
        ["System.Threading.Tasks.Task"] =
        [
            "get_CompletedTask", "FromResult", "FromException", "FromCanceled", "Delay", "WhenAll", "WhenAny", "GetAwaiter", "get_IsCompleted",
            "get_IsCompletedSuccessfully", "get_IsFaulted", "get_IsCanceled", "get_Status", "get_Exception", "get_Result", "get_Id", "Dispose",
            "ToString", "Equals", "GetHashCode",
        ],
        ["System.Threading.Tasks.ValueTask"] =
        [
            ".ctor", "get_CompletedTask", "FromResult", "FromException", "FromCanceled", "AsTask", "Preserve", "GetAwaiter", "get_IsCompleted",
            "get_IsCompletedSuccessfully", "get_IsFaulted", "get_IsCanceled", "get_Result", "Equals", "GetHashCode", "op_Equality", "op_Inequality", "ToString",
        ],
        // No Register/UnsafeRegister: a registered callback runs on whichever thread cancels.
        ["System.Threading.CancellationToken"] =
        [
            ".ctor", "get_None", "get_IsCancellationRequested", "get_CanBeCanceled", "ThrowIfCancellationRequested", "Equals", "GetHashCode",
            "op_Equality", "op_Inequality",
        ],
        // No CancelAfter, no timed constructors and no Cancel(bool): each fires from a timer or runs callbacks off the turn.
        ["System.Threading.CancellationTokenSource"] =
            [".ctor()", "Cancel()", "get_Token", "get_IsCancellationRequested", "CreateLinkedTokenSource", "Dispose", "TryReset"],
        ["System.Runtime.CompilerServices.TaskAwaiter"] = Awaiter,
        ["System.Runtime.CompilerServices.ValueTaskAwaiter"] = Awaiter,
        ["System.Runtime.CompilerServices.AsyncTaskMethodBuilder"] = Builder,
        ["System.Runtime.CompilerServices.AsyncValueTaskMethodBuilder"] = Builder,
        ["System.Runtime.CompilerServices.AsyncVoidMethodBuilder"] = Builder,
        ["System.Runtime.CompilerServices.AsyncIteratorMethodBuilder"] = Builder,
        ["System.Runtime.CompilerServices.PoolingAsyncValueTaskMethodBuilder"] = Builder,
        ["System.Threading.Tasks.Sources.ManualResetValueTaskSourceCore"] =
        [
            "get_Version", "GetResult", "GetStatus", "Reset", "SetResult", "SetException", "OnCompleted",
            "get_RunContinuationsAsynchronously", "set_RunContinuationsAsynchronously",
        ],
        ["System.Threading.Tasks.Sources.IValueTaskSource"] = ["GetResult", "GetStatus", "OnCompleted"],
        ["System.Runtime.ExceptionServices.ExceptionDispatchInfo"] = ["Capture", "Throw"],
    };

    /// <summary>
    /// Allowed members that pass only when every call site is in a compiler-generated state machine (a nested type the compiler names
    /// &lt;Method&gt;d__N, unspeakable in C#): scheduling a continuation by hand is not a unit's business.
    /// </summary>
    public static readonly string[] StateMachineOnly =
    [
        "System.Runtime.CompilerServices.TaskAwaiter.OnCompleted",
        "System.Runtime.CompilerServices.TaskAwaiter.UnsafeOnCompleted",
        "System.Runtime.CompilerServices.ValueTaskAwaiter.OnCompleted",
        "System.Runtime.CompilerServices.ValueTaskAwaiter.UnsafeOnCompleted",
        "System.Runtime.CompilerServices.AsyncTaskMethodBuilder.AwaitOnCompleted",
        "System.Runtime.CompilerServices.AsyncTaskMethodBuilder.AwaitUnsafeOnCompleted",
        "System.Runtime.CompilerServices.AsyncValueTaskMethodBuilder.AwaitOnCompleted",
        "System.Runtime.CompilerServices.AsyncValueTaskMethodBuilder.AwaitUnsafeOnCompleted",
        "System.Runtime.CompilerServices.AsyncVoidMethodBuilder.AwaitOnCompleted",
        "System.Runtime.CompilerServices.AsyncVoidMethodBuilder.AwaitUnsafeOnCompleted",
        "System.Runtime.CompilerServices.AsyncIteratorMethodBuilder.AwaitOnCompleted",
        "System.Runtime.CompilerServices.AsyncIteratorMethodBuilder.AwaitUnsafeOnCompleted",
        "System.Runtime.CompilerServices.PoolingAsyncValueTaskMethodBuilder.AwaitOnCompleted",
        "System.Runtime.CompilerServices.PoolingAsyncValueTaskMethodBuilder.AwaitUnsafeOnCompleted",
        "System.Threading.Tasks.Sources.ManualResetValueTaskSourceCore.OnCompleted",
        "System.Threading.Tasks.Sources.IValueTaskSource.OnCompleted",
    ];

    /// <summary>Members exempt from <see cref="RefusedMembers"/>.</summary>
    public static readonly string[] ExemptMembers =
    [
        "System.Environment.get_NewLine",
        "System.Environment.get_TickCount64",
        "System.Environment.get_CurrentManagedThreadId",
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
        if (Within(type, RefusedTypes))
        {
            return false;
        }

        return AuthoringNamespaces.Contains(ns, StringComparer.Ordinal)
            || Namespaces.Contains(ns, StringComparer.Ordinal)
            || Within(type, Types);
    }

    // The type is one of the names or nested in one.
    private static bool Within(string type, string[] names) =>
        names.Any(name => string.Equals(type, name, StringComparison.Ordinal) || type.StartsWith(name + ".", StringComparison.Ordinal));

    /// <summary>True when the member's type is allowed and the member is not refused.</summary>
    /// <param name="ns">The outermost type's namespace.</param>
    /// <param name="type">The full type name.</param>
    /// <param name="member">The member's metadata name.</param>
    /// <param name="parameters">The member's parameter types, comma-joined, matched by the overload entries of <see cref="MemberAllowList"/>.</param>
    public static bool IsAllowedMember(string ns, string type, string member, string parameters = "")
    {
        if (!IsAllowedType(ns, type))
        {
            return false;
        }

        if (MemberAllowList.TryGetValue(type, out var members))
        {
            var overload = member + "(" + parameters + ")";
            return members.Any(allowed => string.Equals(allowed, member, StringComparison.Ordinal) || string.Equals(allowed, overload, StringComparison.Ordinal));
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
