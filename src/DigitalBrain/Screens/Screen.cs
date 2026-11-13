using System.Reflection;
using System.Text.Json;
using DigitalBrain.Contracts;
using DigitalBrain.Invocation;
using DigitalBrain.Kernel;
using DigitalBrain.Neurons;
using DigitalBrain.UI;
using DigitalBrain.Units;

namespace DigitalBrain;

/// <summary>A screen: a neuron whose state has one stored view. The view's alias is the neuron type.</summary>
public abstract class Screen<TState> : Neuron<TState>, IScreen where TState : class, new()
{
    private PublishedView? _view;
    private string _cause = "reaction";

    /// <summary>The alias the view is stored under and the owner its emits name: the neuron type.</summary>
    protected string Alias => Id.Type;

    /// <summary>The view of <paramref name="state"/>. It runs inside the save, so it cannot await: what it shows is in the state.</summary>
    protected abstract View Render(TState state);

    /// <summary>Saves <paramref name="next"/> and its view as one journal entry; <paramref name="facts"/> follow <see cref="StateChanged"/>.</summary>
    protected Task Show(TState next, params Signal[] facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        return Save(next, [new StateChanged(Id.ToString(), _cause), .. facts]);
    }

    /// <summary>The emit that brings <paramref name="signal"/> back to this screen's <c>[On]</c> reaction.</summary>
    protected Emit Emits<TSignal>(TSignal signal) where TSignal : Signal
    {
        ArgumentNullException.ThrowIfNull(signal);
        return new Emit(Alias, SignalAliases.Of(typeof(TSignal)), JsonSerializer.SerializeToElement(signal, RuntimeProtocol.OptionsFor(typeof(TSignal))));
    }

    /// <summary>A neuron of this brain by its contract's neuron type and a brain-relative <paramref name="key"/>.</summary>
    protected T Get<T>(string key) where T : class => Get<T>(IdOf(typeof(T), key));

    /// <summary>Ensures <typeparamref name="TSignal"/> published by that neuron reaches this screen's <c>[On]</c> reaction. Durable and idempotent.</summary>
    protected Task Follow<TSource, TSignal>(string key) where TSource : class where TSignal : Signal
        => Context.Synapses.Ensure<TSignal>(IdOf(typeof(TSource), key));

    /// <summary>Stores the first view. An override calls the base first.</summary>
    protected override async Task OnActivate(CancellationToken cancellationToken)
    {
        if (_view is not null) { return; }
        _cause = "activate";
        try { await Show(State).ConfigureAwait(true); }
        finally { _cause = "reaction"; }
    }

    /// <inheritdoc />
    public Task<PublishedView?> Open() => Read(Alias);

    /// <inheritdoc />
    public Task Act(ScreenAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return Activate(action.Alias ?? Alias, action.Generation, action.NodeKey, action.FieldValue, Context.Caller?.ToString());
    }

    /// <inheritdoc />
    public Task<PublishedView?> Read(string? alias = null)
        => Task.FromResult(_view is { } view && (alias is null || string.Equals(view.Alias, alias, StringComparison.Ordinal)) ? view : null);

    /// <inheritdoc />
    public async Task Activate(string alias, long generation, string nodeKey, string? fieldValue = null, string? by = null)
    {
        ArgumentNullException.ThrowIfNull(alias);
        ArgumentNullException.ThrowIfNull(nodeKey);
        if (_view is not { } view || !string.Equals(view.Alias, alias, StringComparison.Ordinal) || view.Generation != generation)
        {
            throw new ActivationRefusedException(ActivationRefusedException.Stale);
        }

        var node = ViewNodes.Find(view.View, nodeKey) ?? throw new ActivationRefusedException(ActivationRefusedException.UnknownNode);
        if (ViewNodes.EmitOf(node) is not { } emit)
        {
            throw new ActivationRefusedException(ActivationRefusedException.NoEmit);
        }
        if (!string.Equals(emit.Owner, alias, StringComparison.Ordinal))
        {
            throw new ActivationRefusedException(ActivationRefusedException.ForeignOwner);
        }
        await Handle(emit.Signal, ViewNodes.Payload(emit.Args, fieldValue, by)).ConfigureAwait(true);
    }

    /// <inheritdoc />
    public Task Request(string signal, JsonElement? args = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signal);
        return Handle(signal, ViewNodes.Payload(args, fieldValue: null, by: null));
    }

    private async Task Handle(string alias, JsonElement payload)
    {
        var type = ScreenSignals.Find(GetType(), alias)
            ?? throw new ArgumentException($"Screen '{Alias}' has no [On] reaction for '{alias}'.", nameof(alias));
        var signal = (Signal?)payload.Deserialize(type, RuntimeProtocol.OptionsFor(type))
            ?? throw new ArgumentException($"The payload of '{alias}' is null.", nameof(payload));
        _cause = alias;
        try { _ = await ReactionTable.React(this, signal, CancellationToken.None).ConfigureAwait(true); }
        finally { _cause = "reaction"; }
    }

    internal override async Task<TState?> LoadState()
    {
        var stored = await Seat.Load<StoredState<TState>>().ConfigureAwait(true);
        _view = stored?.View;
        return stored?.State;
    }

    internal override async Task StoreState(TState next, IReadOnlyList<Signal> signals)
    {
        var wire = JsonSerializer.SerializeToElement(Render(next), RuntimeProtocol.OptionsFor(typeof(View)));
        if (System.Text.Encoding.UTF8.GetByteCount(wire.GetRawText()) > UnitLimits.MaxStateBytes)
        {
            throw new InvalidOperationException($"The view of '{Alias}' exceeds the {UnitLimits.MaxStateBytes}-byte cap.");
        }
        if (ViewNodes.DeclaresBy(wire))
        {
            throw new InvalidOperationException($"The view of '{Alias}' declares the host-reserved 'by' in an emit's args.");
        }
        var shown = _view is { } current && current.View.GetRawText() == wire.GetRawText()
            ? current
            : new PublishedView(Alias, (_view?.Generation ?? 0) + 1, wire);
        IReadOnlyList<Signal> entry = ReferenceEquals(shown, _view) ? signals : [.. signals, new ViewChanged(Alias, shown.Generation)];
        await Seat.Save(new StoredState<TState>(next, shown), entry).ConfigureAwait(true);
        _view = shown;
    }

    private NeuronId IdOf(Type contract, string key)
        => new(Id.Brain, PlainNeuronContracts.TypeOf(contract)
            ?? contract.GetCustomAttribute<DefaultNeuronTypeAttribute>(inherit: false)?.Type
            ?? contract.GetCustomAttribute<NeuronAliasAttribute>(inherit: false)?.Alias
            ?? throw new InvalidOperationException($"{contract.Name} names no neuron type."), key);
}

/// <summary>Internal snapshot combining user domain state and published view for persistence.</summary>
internal sealed record StoredState<TState>(TState? State, PublishedView? View) where TState : class;
