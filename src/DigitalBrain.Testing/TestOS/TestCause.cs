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
    private static readonly Lazy<PropertyInfo?> TraceCurrent = new(static () => AppDomain.CurrentDomain.GetAssemblies()
        .FirstOrDefault(assembly => assembly.GetName().Name == "DigitalBrain.Capabilities.Compute")
        ?.GetType("DigitalBrain.Capabilities.Compute.TraceContext")?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static));

    /// <summary>Makes the signal <paramref name="publisher"/> journaled at <paramref name="sequence"/> the cause of this flow.</summary>
    public static void Enter(NeuronId publisher, long sequence, string? traceId)
        => Current.Value = (traceId ?? Request() ?? Guid.NewGuid().ToString("N"), Causation.Address(publisher, sequence));

    /// <summary>The trace and causation ids the signals of a save made now are journaled with.</summary>
    public static (string TraceId, string? CausationId) Stamp()
        => Current.Value is { } cause ? cause : (Request() ?? Guid.NewGuid().ToString("N"), null);

    private static string? Request()
    {
        var trace = TraceCurrent.Value?.GetValue(null);
        return trace?.GetType().GetProperty("TraceId")?.GetValue(trace) as string;
    }
}
