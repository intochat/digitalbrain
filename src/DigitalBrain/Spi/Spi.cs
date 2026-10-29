namespace DigitalBrain.Spi;

// The runtime contract: interfaces only. A runtime implements these; neurons consume them.

/// <summary>What a runtime gives a neuron, attached once before activation.</summary>
public interface INeuronContext
{
    /// <summary>The neuron's id.</summary>
    NeuronId Id { get; }

    /// <summary>The services of the hosting brain.</summary>
    IServiceProvider Services { get; }

    /// <summary>The clock.</summary>
    TimeProvider Time { get; }

    /// <summary>The neuron's persisted state.</summary>
    INeuronState State { get; }

    /// <summary>Timers that live with the activation.</summary>
    ITimers Timers { get; }

    /// <summary>Durable reminders that reactivate the neuron.</summary>
    IReminders Reminders { get; }

    /// <summary>Long-running operations.</summary>
    IOperations Operations { get; }

    /// <summary>The synapses this neuron holds as a subscriber.</summary>
    ISynapses Synapses { get; }

    /// <summary>The brain's signal history.</summary>
    ISignalHistory History { get; }

    /// <summary>Publishes a signal from this neuron.</summary>
    Task Publish(Signal signal);

    /// <summary>A typed interface to another neuron of the same brain.</summary>
    T Get<T>(NeuronId id) where T : class;

    /// <summary>Runs <paramref name="work"/> after the current turn ends.</summary>
    void Defer(Func<Task> work);

    /// <summary>The confirmation gate of this neuron's brain (07 D-03); this neuron is the requester.</summary>
    DigitalBrain.Confirmation.IConfirmations Confirmations { get; }

    /// <summary>Asks a person about <paramref name="plan"/> under the brain's confirmation policy; see <see cref="DigitalBrain.Confirmation.IConfirmations.Confirm"/>.</summary>
    Task<DigitalBrain.Confirmation.ConfirmationResult> Confirm(DigitalBrain.Confirmation.ConfirmationPlan plan, CancellationToken ct = default)
        => Confirmations.Confirm(plan, ct);
}

/// <summary>A neuron's persisted state.</summary>
public interface INeuronState
{
    /// <summary>The stored state, or default when none exists.</summary>
    Task<T?> Load<T>();

    /// <summary>
    /// Stores <paramref name="next"/> and journals <paramref name="signals"/>, in order, as one journal entry:
    /// either both are durable or neither is. Signals reach subscribers only after the entry is durable.
    /// </summary>
    Task Save<T>(T next, IReadOnlyList<Signal> signals);

    /// <summary>Deletes the stored state.</summary>
    Task Clear();
}

/// <summary>Timers that live with the activation. Ticks run as turns.</summary>
public interface ITimers
{
    /// <summary>Starts or replaces the named timer. A null period fires once.</summary>
    void Start(string name, TimeSpan due, TimeSpan? period);

    /// <summary>
    /// Starts or replaces the named timer. A null period fires once. When <paramref name="keepAlive"/> is true the host keeps
    /// the neuron's activation alive until a one-shot timer fires or the timer is stopped; false is the three-argument behaviour.
    /// </summary>
    void Start(string name, TimeSpan due, TimeSpan? period, bool keepAlive);

    /// <summary>Stops the named timer.</summary>
    void Stop(string name);
}

/// <summary>Durable reminders that reactivate the neuron. Ticks run as turns.</summary>
public interface IReminders
{
    /// <summary>Starts or replaces the named reminder.</summary>
    Task Start(string name, TimeSpan due, TimeSpan period);

    /// <summary>Stops the named reminder.</summary>
    Task Stop(string name);

    /// <summary>The names of the registered reminders.</summary>
    Task<IReadOnlyList<string>> Names();
}

/// <summary>Long-running operations a neuron starts from a plan: progress, completion, cancellation and dry runs.</summary>
public interface IOperations
{
    /// <summary>Starts the operation <paramref name="plan"/> describes and returns its handle.</summary>
    Task<IOperation> Start(DigitalBrain.Operations.OperationPlan plan, CancellationToken ct);

    /// <summary>Requests cancellation of a running operation; its handle's <see cref="IOperation.Cancellation"/> fires.</summary>
    Task Cancel(DigitalBrain.Operations.OperationId id, CancellationToken ct);
}

/// <summary>The handle of a running operation.</summary>
public interface IOperation
{
    /// <summary>The operation id.</summary>
    DigitalBrain.Operations.OperationId Id { get; }

    /// <summary>Fires when the operation is cancelled.</summary>
    CancellationToken Cancellation { get; }

    /// <summary>Reports progress as a fraction between 0 and 1.</summary>
    ValueTask Report(double fraction);

    /// <summary>Completes the operation with its result.</summary>
    ValueTask Complete(DigitalBrain.Operations.OperationResult result);
}

/// <summary>Typed proxies to neurons.</summary>
public interface INeuronProxies
{
    /// <summary>A typed proxy to the neuron with this id.</summary>
    T Get<T>(NeuronId id) where T : class;
}

/// <summary>
/// Host-level listeners on the signals of every brain (04-19, R4-19): a host (not a neuron) resolves it from the host's services
/// and hears each matching <c>[Signal]</c> any neuron of any brain saves or publishes from now on, after the append is durable.
/// Delivery is in-memory and best effort (no replay, no cursor): a listener that must not miss a signal holds a synapse instead.
/// Registrations live as long as the host; register at host start so a restarted host listens again.
/// </summary>
public interface IClusterListeners
{
    /// <summary>
    /// Calls <paramref name="handler"/> with the publisher and the signal for every <typeparamref name="TSignal"/> appended in any
    /// brain from now on, in publish order per host. Idempotent per <paramref name="listener"/> and <typeparamref name="TSignal"/>:
    /// a second call with the same pair replaces the handler instead of adding one. A handler that throws is logged and does not
    /// stop the others.
    /// </summary>
    /// <returns>Disposing it stops delivery to this handler (a no-op once the handler was replaced).</returns>
    /// <exception cref="ArgumentException"><typeparamref name="TSignal"/> has no registered <c>[Signal]</c> alias, or <paramref name="listener"/> is blank.</exception>
    Task<IAsyncDisposable> Listen<TSignal>(string listener, Func<NeuronId, TSignal, CancellationToken, Task> handler) where TSignal : Signal;
}
