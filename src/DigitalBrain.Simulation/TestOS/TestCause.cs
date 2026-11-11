using System.Reflection;

namespace DigitalBrain.Testing;

/// <summary>
/// TestOS's ambient cause, the same rule as the host runtime: a save inside a reaction takes the cause's trace id and its
/// <see cref="Causation.Address"/> (also in neurons the reaction calls, which run in its flow); outside one, the request's
/// <c>TraceContext</c> trace id; with neither, a fresh id. <c>TraceContext</c> lives in DigitalBrain.Capabilities.Compute, which this
/// package does not reference, so it is read by reflection when that assembly is loaded.
/// </summary>
internal static class TestCause
{
    private static readonly AsyncLocal<(string TraceId, string CausationId)?> Current = new();

    // The fresh trace id minted for this async flow when it had no cause and no request trace, kept so every later stamp agrees.
    private static readonly AsyncLocal<string?> Fresh = new();

    // Found once the Compute assembly is loaded; looked up again while it is not, so an early miss is not cached.
    private static PropertyInfo? _traceCurrent;

    /// <summary>Makes the signal <paramref name="publisher"/> journaled at <paramref name="sequence"/> the cause of this flow.</summary>
    public static void Enter(NeuronId publisher, long sequence, string? traceId)
        => Current.Value = (traceId ?? Request() ?? Fresh.Value ?? Guid.NewGuid().ToString("N"), Causation.Address(publisher, sequence));

    /// <summary>The trace and causation ids the signals of a save made now are journaled with.</summary>
    public static (string TraceId, string? CausationId) Stamp()
        => Current.Value is { } cause ? cause : (Request() ?? (Fresh.Value ??= Guid.NewGuid().ToString("N")), null);

    private static string? Request()
    {
        var trace = TraceCurrent()?.GetValue(null);
        return trace?.GetType().GetProperty("TraceId")?.GetValue(trace) as string;
    }

    private static PropertyInfo? TraceCurrent()
    {
        if (Volatile.Read(ref _traceCurrent) is { } found) { return found; }
        found = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(assembly => assembly.GetName().Name == "DigitalBrain.Capabilities.Compute")
            ?.GetType("DigitalBrain.Capabilities.Compute.TraceContext")?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static);
        if (found is not null) { Volatile.Write(ref _traceCurrent, found); }
        return found;
    }
}
