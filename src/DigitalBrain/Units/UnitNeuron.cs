using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using DigitalBrain.Invocation;
using DigitalBrain.Kernel;
using DigitalBrain.Neurons;

namespace DigitalBrain.Units;

internal static class UnitLimits
{
    public static readonly TimeSpan ReactionTimeout = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan SlotReactionTimeout = TimeSpan.FromSeconds(30);
    public const int MaxStateBytes = 2 * 1024 * 1024;

    public static void RequireFits<TState>(TState state, NeuronId id)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(state, RuntimeProtocol.OptionsFor(typeof(TState)));
        if (bytes.Length > MaxStateBytes)
        {
            throw new InvalidOperationException(
                $"State '{typeof(TState).Name}' of neuron '{id}' serialized to {bytes.Length} bytes, exceeding the {MaxStateBytes}-byte cap.");
        }
    }
}

internal static partial class UnitLog
{
    [Microsoft.Extensions.Logging.LoggerMessage(EventId = 4140, EventName = "ViewNotStored", Level = Microsoft.Extensions.Logging.LogLevel.Warning,
        Message = "View synapse {Synapse} returned {Type}, not a DigitalBrain.UI.View; its result is not stored or announced.")]
    public static partial void ViewNotStored(Microsoft.Extensions.Logging.ILogger logger, string synapse, string type);

    [Microsoft.Extensions.Logging.LoggerMessage(EventId = 4141, EventName = "ViewDeclaresBy", Level = Microsoft.Extensions.Logging.LogLevel.Warning,
        Message = "View synapse {Synapse} declares the host-reserved \"by\" in an emit's args; the view is refused and not published.")]
    public static partial void ViewDeclaresBy(Microsoft.Extensions.Logging.ILogger logger, string synapse);
}

internal sealed class UnitNeuron<TState> : Neuron<StoredUnit<TState>>, ISynapseReceiver, IViewReader, IStateReader, IStateInspectable
    where TState : class, new()
{
    private readonly ISynapseCatalogSource _catalog;
    private readonly ISlotResolver _slots;
    private readonly string _stateAlias;
    private UnitMachine<TState>? _machine;
    private readonly HashSet<string> _warned = new(StringComparer.Ordinal);
    private SynapseCatalog? _boundTo;
    private UnitFunctions<TState>? _functions;

    public UnitNeuron(ISynapseCatalogSource catalog, ISlotResolver slots)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(slots);
        _catalog = catalog;
        _slots = slots;
        var current = catalog.Of(typeof(TState));
        _stateAlias = current.States.FirstOrDefault(s => s.Value == typeof(TState)).Key
            ?? throw new ArgumentException($"{typeof(TState).FullName} is not a [State] record of the catalog.", nameof(catalog));
    }

    private UnitMachine<TState> Machine => _machine ??= new UnitMachine<TState>(_stateAlias, Id.ToString());

    private UnitFunctions<TState> Functions
    {
        get
        {
            var current = _catalog.Of(typeof(TState));
            if (ReferenceEquals(current, _boundTo) && _functions is not null) { return _functions; }
            _boundTo = current;
            return _functions = UnitFunctions<TState>.Bind(current) ?? _functions
                ?? throw new InvalidOperationException($"No unit functions bound for {typeof(TState).Name}");
        }
    }

    Task<JsonElement?> IStateInspectable.InspectState(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var state = State.State;
        if (state is null) { return Task.FromResult<JsonElement?>(null); }
        var type = typeof(TState);
        return Task.FromResult<JsonElement?>(JsonSerializer.SerializeToElement(state, type, RuntimeProtocol.OptionsFor(type)));
    }

    public Task<PublishedView?> Read(string alias) =>
        Task.FromResult(State.View is { } view && string.Equals(view.Alias, alias, StringComparison.Ordinal) ? view : null);

    Task<JsonElement> IStateReader.Read(string alias) =>
        Task.FromResult(State.State is not null && string.Equals(Functions.Alias, alias, StringComparison.Ordinal)
            ? JsonSerializer.SerializeToElement(State.State, RuntimeProtocol.OptionsFor(typeof(TState)))
            : JsonSerializer.SerializeToElement<object?>(null));

    public Task Receive(Signal signal, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(signal);
        return Turn(signal, null, null, null, ct);
    }

    public Task Receive(JournaledSignal journaled, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(journaled);
        return Turn(journaled.Signal, journaled.TraceId, journaled.CausationId, null, ct,
            fromAsks: journaled.Publisher == AskRules.AsksOf(Id),
            causeAddress: Causation.Address(journaled.Publisher, journaled.Sequence));
    }

    public async Task Activate(string alias, long generation, string nodeKey, string? fieldValue, string? by)
    {
        ArgumentNullException.ThrowIfNull(alias);
        ArgumentNullException.ThrowIfNull(nodeKey);
        var pressed = Machine.Press(State.View, alias, generation, nodeKey, fieldValue, by);
        await Turn(pressed, Context.TraceId, null, said: pressed, CancellationToken.None).ConfigureAwait(true);
    }

    private async Task Turn(Signal signal, string? traceId, string? causationId, Signal? said, CancellationToken ct, bool fromAsks = false, string? causeAddress = null)
    {
        if (!fromAsks && string.Equals(SignalAliases.Of(signal), AskRules.AnsweredAlias, StringComparison.Ordinal)) { return; }

        var functions = Functions;
        var call = new UnitCall(signal, traceId, causationId);
        var step = Machine.Start(State);

        try
        {
            foreach (var reaction in functions.ReactionsTo(signal))
            {
                await Run(reaction, call, step.State, causeAddress, result =>
                {
                    var next = Machine.Reacted(step, signal, result);
                    if (result is Reaction<TState> { Asks.Count: > 0 } withAsks)
                    {
                        var asked = new Asked(Id.Type, Guid.NewGuid().ToString("N"), AskRules.Checked(withAsks.Asks, functions.Descriptors));
                        next = next with { Signals = [.. next.Signals, asked] };
                    }
                    var outcome = ReferenceEquals(next, step) ? "unchanged" : "saved";
                    step = next;
                    return Task.FromResult(outcome);
                }, ct).ConfigureAwait(true);
            }

            if (step.Changed) { UnitLimits.RequireFits(step.State, Id); }
        }
        catch when (said is not null)
        {
            await Publish(said).ConfigureAwait(true);
            throw;
        }

        ExceptionDispatchInfo? viewFailed = null;
        if (step.Changed)
        {
            foreach (var view in functions.Views)
            {
#pragma warning disable CA1031
                try
                {
                    await Run(view, UnitCall.None, step.State, null, result =>
                    {
                        (step, var outcome) = Machine.Viewed(step, result);
                        if (outcome == ViewOutcome.DeclaresBy) { UnitLog.ViewDeclaresBy(Logger(), view.Name); }
                        else if (outcome == ViewOutcome.NotAView && _warned.Add(view.Name)) { UnitLog.ViewNotStored(Logger(), view.Name, result?.GetType().FullName ?? "null"); }
                        return Task.FromResult(outcome == ViewOutcome.DeclaresBy ? "refused" : "viewed");
                    }, CancellationToken.None).ConfigureAwait(true);
                }
                catch (Exception error) { viewFailed ??= ExceptionDispatchInfo.Capture(error); }
#pragma warning restore CA1031
            }
        }

        if (step.Changed)
        {
            await Save(step.Stored, said is null ? step.Signals : [said, .. step.Signals]).ConfigureAwait(true);
        }
        else if (said is not null)
        {
            await Publish(said).ConfigureAwait(true);
        }

        viewFailed?.Throw();
    }

    private async Task Run(BoundFunction function, UnitCall call, TState state, string? causeAddress, Func<object?, Task<string>> settle, CancellationToken ct)
    {
        var neuron = Id.ToString();
        var arriving = call.Signal is null ? Functions.Alias : SignalAliases.Of(call.Signal);
        await PublishEphemeral(new SynapseStarted(neuron, function.Name, arriving)).ConfigureAwait(true);
        var clock = Stopwatch.StartNew();
        string outcome;

        try
        {
            var result = await function.Invoke(call, state, _slots, Id, ct).ConfigureAwait(true);
            outcome = await settle(result).ConfigureAwait(true);
        }
        catch (Exception error)
        {
            await PublishEphemeral(new SynapseFinished(neuron, function.Name, "failed", clock.Elapsed.TotalMilliseconds)).ConfigureAwait(true);
            if (!string.Equals(arriving, "synapse.failed", StringComparison.Ordinal))
            {
                var message = error.Message.Length > 500 ? error.Message[..500] : error.Message;
#pragma warning disable CA1031
                try { await Publish(new SynapseFailed(Functions.Alias, function.Name, arriving, causeAddress, error.GetType().Name, message)).ConfigureAwait(true); }
                catch { }
#pragma warning restore CA1031
            }
            throw;
        }

        await PublishEphemeral(new SynapseFinished(neuron, function.Name, outcome, clock.Elapsed.TotalMilliseconds)).ConfigureAwait(true);
    }

    private Microsoft.Extensions.Logging.ILogger Logger()
    {
        try { return (Seat.Services.GetService(typeof(Microsoft.Extensions.Logging.ILoggerFactory)) as Microsoft.Extensions.Logging.ILoggerFactory)?.CreateLogger("DigitalBrain.Synapses") ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance; }
        catch { return Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance; }
    }
}
