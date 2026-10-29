using Orleans;

namespace DigitalBrain.Runtime.Orleans.Tests.Integration.Probe;

/// <summary>The baseline for HostCostFacts: a precompiled typed Orleans grain with the probe's Add and Echo (test assembly only).</summary>
public interface ITypedProbe : IGrainWithStringKey
{
    Task<int> Add(int a, int b);

    ValueTask<string> Echo(string text);
}

public sealed class TypedProbeGrain : Grain, ITypedProbe
{
    public Task<int> Add(int a, int b) => Task.FromResult(a + b);

    public ValueTask<string> Echo(string text) => ValueTask.FromResult(text);
}
