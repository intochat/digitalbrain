using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using DigitalBrain.Abstractions;
using DigitalBrain.Client;
using Xunit;

namespace DigitalBrain.Tests.Client;

public sealed class SendOrdering
{
    // Connect is Orleans grain-factory wiring; ConnectAsync is the unrelated single-file behavior SDK entry.
#pragma warning disable CA1849

    [Fact(DisplayName = "neuron reference one-way Send rejects a null synapse before any grain call")]
    public async Task ReferenceSendRejectsNullSynapseBeforeAnyGrainCall()
    {
        var calls = new GrainCallRecorder();
        var client = DigitalBrainClient.Connect(calls.Factory, "owner");

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => client.Get<ISendTarget>("target").SendAsync(null!, TestContext.Current.CancellationToken));

        Assert.Empty(calls.Calls);
    }

    [Fact(DisplayName = "neuron reference one-way Send activates once before firing once")]
    public async Task ReferenceSendActivatesOnceBeforeFiringOnce()
    {
        var calls = new GrainCallRecorder();
        var client = DigitalBrainClient.Connect(calls.Factory, "owner");

        await client.Get<ISendTarget>("target").SendAsync(new TestSynapse(), TestContext.Current.CancellationToken);

        Assert.Equal(
            ["brain", "activate", "session", "fire"],
            calls.Calls);
    }

    [Fact(DisplayName = "raw Send activates once before firing once")]
    public async Task RawSendActivatesOnceBeforeFiringOnce()
    {
        var calls = new GrainCallRecorder();
        var client = DigitalBrainClient.Connect(calls.Factory, "owner");
        var receiver = NeuronId.For<ISendTarget>(client.Owner, "target");

        await client.SendAsync(receiver, new TestSynapse(), TestContext.Current.CancellationToken);

        Assert.Equal(
            ["brain", "activate", "session", "fire"],
            calls.Calls);
    }

    [Fact(DisplayName = "typed request Send activates, fires, then watches the session journal for the correlated response")]
    public async Task TypedRequestSendWatchesSessionJournalForCorrelatedResponse()
    {
        var calls = new GrainCallRecorder();
        var client = DigitalBrainClient.Connect(calls.Factory, "owner");
        var response = new TestResponse("ok");
        calls.Response = response;

        var result = await client.Get<ISendTarget>("target")
            .SendAsync(new TestRequest("q"), TestContext.Current.CancellationToken);

        Assert.Same(response, result);
        Assert.Contains("watch", calls.Calls);
        Assert.Contains("fire", calls.Calls);
        Assert.Contains("unwatch", calls.Calls);
        Assert.True(calls.Calls.IndexOf("watch") < calls.Calls.IndexOf("fire"));
        Assert.True(calls.Calls.IndexOf("fire") < calls.Calls.IndexOf("unwatch"));
    }

    [Fact(DisplayName = "typed request Send cancellation tears down the journal watch after fire")]
    public async Task TypedRequestSendCancellationTearsDownWatch()
    {
        var calls = new GrainCallRecorder();
        var client = DigitalBrainClient.Connect(calls.Factory, "owner");
        calls.HoldWatch = true;

        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        var send = client.Get<ISendTarget>("target").SendAsync(new TestRequest("q"), cancellation.Token);
        await calls.WatchStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await send.ConfigureAwait(true));
        Assert.Contains("unwatch", calls.Calls);
        Assert.Contains("fire", calls.Calls);
    }

#pragma warning restore CA1849

    private sealed record TestSynapse : Synapse;

    private sealed record TestRequest(string Query) : RequestSynapse<TestResponse>;

    private sealed record TestResponse(string Text) : Synapse;
}

internal interface ISendTarget : INeuron;

internal sealed class GrainCallRecorder
{
    public List<string> Calls { get; } = [];

    public IGrainFactory Factory { get; }

    public Synapse? Response { get; set; }

    public bool HoldWatch { get; set; }

    public TaskCompletionSource WatchStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public IJournalObserver? Observer { get; set; }

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

        if (targetMethod.Name == nameof(IGrainFactory.CreateObjectReference) && args is { Length: > 0 })
        {
            return args[0];
        }

        if (targetMethod.Name == nameof(IGrainFactory.DeleteObjectReference))
        {
            return null;
        }

        var grainType = targetMethod.GetGenericArguments().Single();
        if (grainType == typeof(IDigitalBrainNeuron))
        {
            Calls.Calls.Add("brain");
            return RecordingGrain.Create<IDigitalBrainNeuron>(Calls, "activate");
        }

        if (grainType == typeof(ISessionNeuron))
        {
            Calls.Calls.Add("session");
            return RecordingSession.Create(Calls);
        }

        throw new NotSupportedException($"Unexpected grain contract '{grainType.Name}'.");
    }

    private GrainCallRecorder Calls
        => _calls ?? throw new InvalidOperationException("Recorder was not initialized.");

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
    private GrainCallRecorder? _calls;
    private string? _operation;

    public static T Create<T>(GrainCallRecorder calls, string operation)
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
        _calls?.Calls.Add(_operation ?? throw new InvalidOperationException("Operation was not initialized."));
        return Task.CompletedTask;
    }

    private void Initialize(GrainCallRecorder calls, string operation)
    {
        _calls = calls;
        _operation = operation;
    }
}

[SuppressMessage(
    "Performance",
    "CA1852:Seal internal types",
    Justification = "DispatchProxy derives the recorder at runtime.")]
internal class RecordingSession : DispatchProxy
{
    private GrainCallRecorder? _calls;

    public static ISessionNeuron Create(GrainCallRecorder calls)
    {
        var implementation = new RecordingSession();
        GC.KeepAlive(implementation);

        var proxy = DispatchProxy.Create<ISessionNeuron, RecordingSession>();
        ((RecordingSession)(object)proxy)._calls = calls;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);
        var calls = _calls ?? throw new InvalidOperationException("Recorder was not initialized.");

        if (targetMethod.Name == nameof(ISessionNeuron.Fire))
        {
            calls.Calls.Add("fire");
            var synapse = (Synapse)args![1]!;
            var delivery = SynapseDelivery.Create(
                synapse,
                ISessionNeuron.ForOwner(new OwnerId("owner")),
                sequence: 1);

            if (!calls.HoldWatch
                && calls.Response is { } response
                && calls.Observer is { } observer)
            {
                var responseDelivery = SynapseDelivery.Create(
                    response,
                    NeuronId.For<ISendTarget>(new OwnerId("owner"), "target"),
                    sequence: 1,
                    cause: delivery,
                    correlation: delivery.CorrelationId);
                _ = observer.ObserveAsync(
                    JournalKind.Incoming,
                    new JournalRead(1, [responseDelivery], ResetSnapshot: null));
            }

            return Task.FromResult(delivery);
        }

        if (targetMethod.Name == nameof(ISessionNeuron.WatchNeuron))
        {
            calls.Calls.Add("watch");
            calls.Observer = (IJournalObserver)args![3]!;
            calls.WatchStarted.TrySetResult();
            return Task.CompletedTask;
        }

        if (targetMethod.Name == nameof(ISessionNeuron.UnwatchNeuron))
        {
            calls.Calls.Add("unwatch");
            return Task.CompletedTask;
        }

        if (targetMethod.Name == nameof(ISessionNeuron.ReadNeuronJournal))
        {
            return Task.FromResult(new JournalRead(0, [], ResetSnapshot: null));
        }

        if (targetMethod.Name == nameof(ISessionNeuron.Emit))
        {
            return Task.CompletedTask;
        }

        throw new NotSupportedException($"Unexpected session method '{targetMethod.Name}'.");
    }
}
