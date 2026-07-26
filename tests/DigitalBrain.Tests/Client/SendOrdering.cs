using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using DigitalBrain.Abstractions;
using DigitalBrain.Client;
using DigitalBrain.Quickstart;
using Xunit;

namespace DigitalBrain.Tests.Client;

public sealed class SendOrdering
{
    [Fact(DisplayName = "typed Send rejects a null synapse before any grain call")]
    public async Task TypedSendRejectsNullSynapseBeforeAnyGrainCall()
    {
        var calls = new GrainCallRecorder();
        var client = DigitalBrainClient.Connect(calls.Factory, "owner");

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => client.SendAsync<IGreeter>("greeter", null!));

        Assert.Empty(calls.Calls);
    }

    [Fact(DisplayName = "typed Send activates once before firing once")]
    public async Task TypedSendActivatesOnceBeforeFiringOnce()
    {
        var calls = new GrainCallRecorder();
        var client = DigitalBrainClient.Connect(calls.Factory, "owner");

        await client.SendAsync<IGreeter>("greeter", new TestSynapse());

        Assert.Equal(
            ["brain", "activate", "session", "fire"],
            calls.Calls);
    }

    [Fact(DisplayName = "raw Send activates once before firing once")]
    public async Task RawSendActivatesOnceBeforeFiringOnce()
    {
        var calls = new GrainCallRecorder();
        var client = DigitalBrainClient.Connect(calls.Factory, "owner");
        var receiver = NeuronId.For<IGreeter>(client.Owner, "greeter");

        await client.SendAsync(receiver, new TestSynapse());

        Assert.Equal(
            ["brain", "activate", "session", "fire"],
            calls.Calls);
    }

    private sealed record TestSynapse : Synapse;

}

internal sealed class GrainCallRecorder
{
    public List<string> Calls { get; } = [];

    public IGrainFactory Factory { get; }

    public GrainCallRecorder()
    {
        Factory = RecordingGrainFactory.Create(this);
    }
}

[SuppressMessage(
    "Performance",
    "CA1852:Seal internal types",
    Justification = "DispatchProxy derives the recorder at runtime.")]
internal class RecordingGrainFactory : DispatchProxy
{
    private GrainCallRecorder? _calls;

    public static IGrainFactory Create(GrainCallRecorder calls)
    {
        var implementation = new RecordingGrainFactory();
        GC.KeepAlive(implementation);

        var proxy = DispatchProxy.Create<IGrainFactory, RecordingGrainFactory>();
        ((RecordingGrainFactory)(object)proxy).Initialize(calls);
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);

        var grainType = targetMethod.GetGenericArguments().Single();
        if (grainType == typeof(IDigitalBrainNeuron))
        {
            Calls.Add("brain");
            return RecordingGrain.Create<IDigitalBrainNeuron>(Calls, "activate");
        }

        if (grainType == typeof(ISessionNeuron))
        {
            Calls.Add("session");
            return RecordingGrain.Create<ISessionNeuron>(Calls, "fire");
        }

        throw new NotSupportedException($"Unexpected grain contract '{grainType.Name}'.");
    }

    private List<string> Calls
        => _calls?.Calls ?? throw new InvalidOperationException("Recorder was not initialized.");

    private void Initialize(GrainCallRecorder calls)
    {
        _calls = calls;
    }
}

[SuppressMessage(
    "Performance",
    "CA1852:Seal internal types",
    Justification = "DispatchProxy derives the recorder at runtime.")]
internal class RecordingGrain : DispatchProxy
{
    private List<string>? _calls;
    private string? _operation;

    public static T Create<T>(List<string> calls, string operation)
        where T : class
    {
        var implementation = new RecordingGrain();
        GC.KeepAlive(implementation);

        var proxy = DispatchProxy.Create<T, RecordingGrain>();
        ((RecordingGrain)(object)proxy).Initialize(calls, operation);
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        _calls?.Add(_operation ?? throw new InvalidOperationException("Operation was not initialized."));
        return Task.CompletedTask;
    }

    private void Initialize(List<string> calls, string operation)
    {
        _calls = calls;
        _operation = operation;
    }
}
