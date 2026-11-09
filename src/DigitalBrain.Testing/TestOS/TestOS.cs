using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DigitalBrain.Contracts;
using DigitalBrain.Invocation;
using DigitalBrain.Spi;
using DigitalBrain.Time;

namespace DigitalBrain.Testing;

/// <summary>What a <see cref="TestOS"/> does: history retention for <see cref="TestOS.ForceCheckpoint(BrainId)"/>, delivery faults and call limits.</summary>
public sealed class TestOSOptions
{
    /// <summary>Signals per publisher a checkpoint keeps at least. Default 10 000, as on the host runtime.</summary>
    public int HistoryWindowSignals { get; set; } = 10_000;

    /// <summary>Signals younger than this survive a checkpoint. Default 7 days.</summary>
    public TimeSpan HistoryWindowAge { get; set; } = TimeSpan.FromDays(7);

    /// <summary>A cursor not advanced for this long stops pinning the floor. Default 7 days.</summary>
    public TimeSpan StaleCursorAfter { get; set; } = TimeSpan.FromDays(7);

    /// <summary>Delivers every signal reference twice, as an at-least-once stream may; reactions must still see each signal once.</summary>
    public bool DuplicateDelivery { get; set; }

    /// <summary>How long an awaited call waits on the TestOS clock before it fails with <see cref="TimeoutException"/>. Default 30 seconds.</summary>
    public TimeSpan CallTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How long a gate synapse may take before the publish fails closed; default half of <see cref="CallTimeout"/>.</summary>
    public TimeSpan GateTimeout
    {
        get => _gateTimeout ?? CallTimeout / 2;
        set => _gateTimeout = value;
    }

    private TimeSpan? _gateTimeout;

    /// <summary>The clock's start, or 2026-01-01 UTC.</summary>
    public DateTimeOffset? Start { get; set; }

    /// <summary>The services neuron constructors and <c>Context.Services</c> resolve from.</summary>
    public IServiceProvider? Services { get; set; }

    /// <summary>
    /// A test-only delay before a gate's <c>Committed</c> turn: awaited with the gate neuron and the published signal before each
    /// allowing gate is told the publisher's append is durable, so a fact can force gate commits out of order. Default null (no delay).
    /// </summary>
    public Func<NeuronId, Signal, Task>? BeforeGateCommit { get; set; }
}

/// <summary>
/// The in-process test runtime: hosts plain <see cref="Neuron"/> classes with the framework's turn semantics and delivery contract,
/// with no actor framework, no network and a clock that moves only when told. One reaction or call at a time per neuron unless the neutral
/// scheduling attributes say otherwise; reactions ordered per source and deduped by cursor; timers and reminders as turns.
/// There is no background checkpoint: <see cref="ForceCheckpoint(BrainId)"/> moves the history floor.
/// </summary>
public sealed partial class TestOS : IAsyncDisposable, IBrainObserver
{
    private readonly ConcurrentDictionary<string, Type> _types = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<NeuronId, TestNeuronHost> _hosts = new();
    private readonly ConcurrentDictionary<BrainId, TestJournal> _journals = new();
    private readonly ConcurrentQueue<Exception> _failures = new();
    private readonly Lock _gate = new();
    private int _outstanding;
    private string? _failNextPublish;
    private TaskCompletionSource _quiet = Quiet();
    private readonly Lazy<DigitalBrain.Runtime.Synapses.SynapseCompiler> _compiler =
        new(() => new DigitalBrain.Runtime.Synapses.SynapseCompiler(DigitalBrain.Runtime.Synapses.ReferenceSet.FromDefaultContext()));
    private (DigitalBrain.Runtime.Synapses.SynapseHost Host, ISynapseRestorer Restorer)? _runtime;
    private readonly ConcurrentDictionary<string, byte> _generated = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ActionDescriptor> _actions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Assembly, byte> _scannedModules = new();

    private TestOS(TestOSOptions options)
    {
        Options = options;
        Clock = new TestClock(options.Start);
        Capabilities = new CapabilityFakes();
        Source = new SynapseCatalogSource(SynapseCatalog.Scan());
        Services = new TestOSServices(Capabilities, options.Services ?? EmptyServices.Instance, new TestInstallHooks(this), new TestSlotResolver(this), Source, () => [.. _actions.Values], () => Restorer);
        Delivery = new SynapseRegistry(this);
        Confirmations = new TestConfirmations(Clock);
        Listeners = new TestClusterListeners(this);
        Gates = new InstalledGateIndex(Source);
        Registrar = new TestSynapseRegistrar(this);
        Store = new DigitalBrain.Runtime.Synapses.NeuronSourceStore(brain => Get<ISources>(new NeuronId(brain, SourcesType, "default")));
        Register<InstallsNeuron>();
        Register<SourcesNeuron>();
        Register<PoliciesNeuron>();
        Register<TimeNeuron>();
        _ = Listeners.Listen<GateChanged>("testos.installed-gates", RefreshGates);
    }

    /// <summary>The <c>[State]</c> records and synapse functions in force now: <see cref="Source"/>'s current catalog.</summary>
    public SynapseCatalog Catalog => Source.Current;

    /// <summary>
    /// The live catalog every state neuron, the installs neuron and <see cref="Gates"/> read on each turn or call, so a change reaches
    /// neurons activated before it. <see cref="Register(Assembly)"/> merges into it and <see cref="Unregister(string)"/> removes from it.
    /// </summary>
    public SynapseCatalogSource Source { get; }

    /// <summary>
    /// The gate index a durable signal passes before it is appended: the <see cref="Catalog"/> gates of the aliases the brain's
    /// <see cref="IInstalls"/> neuron records, refreshed on every <see cref="GateChanged"/>. A brain with no install has no gates.
    /// </summary>
    public InstalledGateIndex Gates { get; }

    /// <summary>
    /// Makes a loaded synapse source live on this TestOS, or removes it: <see cref="Source"/>, the state neuron classes and the hosts of a
    /// replaced alias. TestOS journals typed signal objects, so a generated <c>[Signal]</c> needs no registration.
    /// </summary>
    public ISynapseRegistrar Registrar { get; }

    /// <summary>
    /// The synapse host of this TestOS: deploys generated sources through <see cref="Registrar"/>, <see cref="Source"/>, each brain's
    /// <see cref="IInstalls"/> neuron, <see cref="Store"/> and <see cref="ClusterListeners"/>. Created on first use.
    /// </summary>
    public DigitalBrain.Runtime.Synapses.SynapseHost Synapses => Runtime().Host;

    /// <summary>Where <see cref="Synapses"/> keeps the deployed sources: each brain's <see cref="ISources"/> neuron, so they survive <see cref="Restart"/>.</summary>
    public ISourceStore Store { get; }

    /// <summary>
    /// Restores a generated source when an activation misses its alias, as after <see cref="Restart"/>: the <see cref="ISynapseRestorer"/>
    /// of <see cref="TestOSOptions.Services"/> if any, else one that loads the stored build through <see cref="Registrar"/> and gives it to
    /// <see cref="Synapses"/>.
    /// </summary>
    public ISynapseRestorer Restorer => Runtime().Restorer;

    /// <summary>Host-level listeners on the signals of every brain of this TestOS (04-19); they survive <see cref="Restart"/>.</summary>
    public DigitalBrain.Spi.IClusterListeners ClusterListeners => Listeners;

    internal TestClusterListeners Listeners { get; }

    /// <summary>
    /// The trusted caller stamp this TestOS puts on every synapse admission (<see cref="SynapseRequest.Caller"/>), as a host's
    /// trusted edge stamps it on the runtime; null (the default) admits with no stamp. Set it to test an <c>AdmitSynapse</c> that
    /// authorizes by caller.
    /// </summary>
    public DigitalBrain.Capabilities.CapabilityCaller? Caller { get; set; }

    /// <summary>The confirmation gate every neuron's <c>Context.Confirm</c> asks (07 D-03); set its policy and answer as a person here.</summary>
    public TestConfirmations Confirmations { get; }

    /// <summary>The options this TestOS runs with.</summary>
    public TestOSOptions Options { get; }

    /// <summary>The clock every neuron, timer, reminder and call timeout runs on.</summary>
    public TestClock Clock { get; }

    /// <summary>The services neurons and apps resolve from: bound capability fakes, an <see cref="DigitalBrain.Capabilities.ICapabilityBroker"/> over them, then <see cref="TestOSOptions.Services"/>.</summary>
    public IServiceProvider Services { get; }

    /// <summary>The capability fakes bound with <see cref="With{TCapability}(string, TCapability)"/>.</summary>
    public CapabilityFakes Capabilities { get; }

    /// <summary>Errors of work nobody awaited: one-way calls, deferred work, timer ticks and reactions.</summary>
    public IReadOnlyCollection<Exception> Failures => _failures;

    /// <summary>How many duplicate signal references the cursor dedupe dropped.</summary>
    public long DuplicatesDropped => Delivery.DuplicatesDropped;

    internal NeuronBinder Binder { get; } = new();

    internal SynapseRegistry Delivery { get; }

    /// <summary>A local TestOS.</summary>
    public static TestOS Local(Action<TestOSOptions>? configure = null)
    {
        var options = new TestOSOptions();
        configure?.Invoke(options);
        ArgumentOutOfRangeException.ThrowIfNegative(options.HistoryWindowSignals);
        return new TestOS(options);
    }

    /// <summary>Registers a <c>[Neuron("type")]</c> class.</summary>
    /// <exception cref="ArgumentException">The class has no <see cref="NeuronAttribute"/>, or its type name is taken by another class.</exception>
    public TestOS Register<TNeuron>() where TNeuron : Neuron => Register(typeof(TNeuron));

    /// <summary>
    /// Registers every concrete <c>[Neuron("type")]</c> class of <paramref name="assembly"/>, merges its <c>[State]</c> records and synapse
    /// functions into <see cref="Catalog"/>, and hosts each state alias as a <see cref="StateNeuron{TState}"/>.
    /// </summary>
    /// <exception cref="SynapseShapeException">A synapse of unsupported shape, or a state alias already in <see cref="Catalog"/>.</exception>
    public TestOS Register(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ScanModuleActions(assembly);
        var scanned = SynapseCatalog.Scan(assembly);
        Source.Merge(scanned);
        foreach (var (alias, record) in scanned.States)
        {
            var neuron = typeof(StateNeuron<>).MakeGenericType(record);
            var registered = _types.GetOrAdd(alias, neuron);
            if (registered != neuron) { throw new ArgumentException($"State alias '{alias}' is already registered for {registered.FullName}.", nameof(assembly)); }
        }
        foreach (var type in assembly.GetTypes().Where(type => type is { IsAbstract: false, IsClass: true } && typeof(Neuron).IsAssignableFrom(type) && type.IsDefined(typeof(NeuronAttribute), false)))
        {
            Register(type);
        }
        return this;
    }

    /// <summary>
    /// Removes the state alias <paramref name="alias"/> from <see cref="Source"/> and its state neuron class, so its gates and synapses stop
    /// at once; activations already hosted stay until they end. A plain <c>[Neuron]</c> type name or an unknown alias only leaves the catalog.
    /// </summary>
    /// <param name="alias">The state alias.</param>
    /// <returns>This TestOS.</returns>
    public TestOS Unregister(string alias)
    {
        ArgumentNullException.ThrowIfNull(alias);
        Source.Remove(alias);
        if (_types.TryGetValue(alias, out var type) && type.IsGenericType && type.GetGenericTypeDefinition() == typeof(StateNeuron<>))
        {
            _ = _types.TryRemove(new KeyValuePair<string, Type>(alias, type));
        }
        return this;
    }

    /// <summary>A typed proxy to <paramref name="id"/>. Calls go through <see cref="NeuronProxy"/> and <see cref="NeuronBinder"/> as JSON.</summary>
    /// <exception cref="InvalidOperationException">No class is registered for the id's type.</exception>
    public T Get<T>(NeuronId id) where T : class
    {
        _ = Host(id);
        return NeuronProxy.Create<T>(id, new CallerSink(this, null));
    }

    /// <summary>Activates <paramref name="id"/> if it is not active; on activation it catches up on its synapses.</summary>
    public Task Activate(NeuronId id) => Track(Host(id).Activate());

    /// <summary>Ends the activation of <paramref name="id"/> after its current turn; state, synapses and reminders survive.</summary>
    public Task Deactivate(NeuronId id) => Track(Host(id).Deactivate(DeactivationCause.Requested));

    /// <summary>True when <paramref name="id"/> has a live activation.</summary>
    public bool IsActive(NeuronId id) => _hosts.TryGetValue(id, out var host) && host.IsActive;

    /// <summary>In-process neurons whose class is <typeparamref name="T"/> or whose type name <typeparamref name="T"/> declares.</summary>
    public async IAsyncEnumerable<BrainInstance> Instances<T>([EnumeratorCancellation] CancellationToken cancellationToken) where T : class
    {
        var named = typeof(T).GetCustomAttribute<NeuronAttribute>(false)?.Type
            ?? typeof(T).GetCustomAttribute<DefaultNeuronTypeAttribute>(false)?.Type;
        foreach (var host in _hosts.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (typeof(T) == typeof(object) || typeof(T).IsAssignableFrom(host.Class) || named is not null && host.Id.Type == named)
            { yield return InstanceOf(host); }
        }
        await Task.CompletedTask;
    }

    /// <summary>In-process neurons, or those of one neuron type when <paramref name="type"/> is set.</summary>
    public async IAsyncEnumerable<BrainInstance> Instances(string? type, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var host in _hosts.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (type is null || host.Id.Type == type) { yield return InstanceOf(host); }
        }
        await Task.CompletedTask;
    }

    /// <summary>The neuron classes registered on this TestOS.</summary>
    public Task<BrainType[]> Types(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_types.Select(pair => new BrainType(
            pair.Value.FullName ?? pair.Key, pair.Key, TypeKind.Neuron, "test", "", [], [])).ToArray());
    }

    /// <summary>Brains that have an in-process neuron.</summary>
    public Task<string[]> Brains(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_hosts.Keys.Select(id => id.Brain.Value!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
    }

    /// <summary>Every synapse this TestOS holds. A TestOS has no stamped caller, so the list is not limited to one brain.</summary>
    Task<ObservedSynapse[]> IBrainObserver.Synapses(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Delivery.All());
    }

    /// <summary>Deliveries from now on, across every brain of this TestOS.</summary>
    public IAsyncEnumerable<SignalDelivery> Deliveries(CancellationToken cancellationToken = default) => _deliveries.Listen(cancellationToken);

    /// <summary>Inspects the live state of an in-process neuron.</summary>
    Task<System.Text.Json.JsonElement?> IBrainObserver.State(NeuronId id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_hosts.TryGetValue(id, out var host) && host.CurrentNeuron is IStateInspectable inspectable)
        {
            return inspectable.InspectState(cancellationToken);
        }
        return Task.FromResult<System.Text.Json.JsonElement?>(null);
    }

    private readonly DeliveryFanout _deliveries = new();

    internal void NoteDelivery(SignalDelivery delivery) => _deliveries.Publish(delivery);

    private static BrainInstance InstanceOf(TestNeuronHost host)
        => new(host.Id.Type, host.Id.ToString(), host.Created, null);

    /// <summary>Completes once no turn, delivery, tick or deferred work is queued or running.</summary>
    public async Task Idle()
    {
        while (true)
        {
            Task quiet;
            lock (_gate) { quiet = _quiet.Task; }
            await quiet.ConfigureAwait(false);
            await Task.Yield();
            lock (_gate)
            {
                if (_outstanding == 0) { return; }
            }
        }
    }

    /// <summary>The journal of <paramref name="brain"/>.</summary>
    public TestJournal Journal(BrainId brain) => _journals.GetOrAdd(brain, key => new TestJournal(key, Clock));

    /// <summary>
    /// Runs a checkpoint of <paramref name="brain"/> now: each publisher's floor moves to
    /// <c>max(0, min(lowest live cursor, last - max(HistoryWindowSignals, signals newer than HistoryWindowAge)))</c>, ages on <see cref="Clock"/>.
    /// </summary>
    public void ForceCheckpoint(BrainId brain)
    {
        var journal = Journal(brain);
        foreach (var publisher in journal.Publishers())
        {
            journal.Checkpoint(publisher, Options, Delivery.LowestLiveCursor(publisher));
        }
    }

    /// <summary>
    /// Makes exactly the next journal append of <paramref name="publisher"/> send no signal references and the saving call throw
    /// after the append is durable, as a host killed between write and publish would. Subscribers catch up on activation or
    /// <see cref="Restart"/>.
    /// </summary>
    public void FailNextPublishAfterAppend(NeuronId publisher) => Volatile.Write(ref _failNextPublish, publisher.ToString());

    /// <summary>
    /// Stops every activation, as a host restart does, keeping the journal, synapses, cursors and reminders; then wakes each
    /// subscriber whose cursor is behind a source's last signal, as the runtime's start-up reconcile does. The loaded generated sources
    /// are forgotten (their aliases leave the registry and <see cref="Synapses"/>), so the next activation of one restores it.
    /// </summary>
    public async Task Restart()
    {
        await Idle().ConfigureAwait(false);
        foreach (var host in _hosts.Values.Where(host => host.IsActive).ToList())
        {
            await Track(host.Deactivate(DeactivationCause.Shutdown)).ConfigureAwait(false);
        }
        await ForgetGenerated().ConfigureAwait(false);
        Gates.Reset();
        foreach (var subscriber in Delivery.Behind())
        {
            await Activate(subscriber).ConfigureAwait(false);
        }
        await Idle().ConfigureAwait(false);
    }

    internal bool TakePublishFault(NeuronId publisher)
    {
        var armed = Volatile.Read(ref _failNextPublish);
        return armed is not null && armed == publisher.ToString() && Interlocked.CompareExchange(ref _failNextPublish, null, armed) == armed;
    }

    /// <summary>The synapses <paramref name="subscriber"/> holds.</summary>
    public IReadOnlyList<Synapse> SynapsesOf(NeuronId subscriber) => Delivery.Of(subscriber);

    /// <summary>The last sequence of <paramref name="source"/> that <paramref name="subscriber"/> has reacted to, or null without a synapse.</summary>
    public long? Cursor(NeuronId subscriber, NeuronId source) => Delivery.Cursor(subscriber, source);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_runtime is { } runtime) { await runtime.Host.DisposeAsync().ConfigureAwait(false); }
        foreach (var host in _hosts.Values)
        {
            try { await host.Deactivate(DeactivationCause.Shutdown).ConfigureAwait(false); }
            catch (Exception error) when (error is not OutOfMemoryException) { Fail(error); }
            host.Dispose();
        }
    }

    internal TestNeuronHost Host(NeuronId id)
    {
        if (_hosts.TryGetValue(id, out var host)) { return host; }
        if (!_types.TryGetValue(id.Type, out var type) && !(Restore(id) && _types.TryGetValue(id.Type, out type)))
        {
            throw new InvalidOperationException($"No neuron class is registered for type '{id.Type}'. Call Register<TNeuron>() first.");
        }
        var created = new TestNeuronHost(this, id, type);
        var added = _hosts.GetOrAdd(id, created);
        if (!ReferenceEquals(added, created)) { created.Dispose(); }
        return added;
    }

    // On a miss, Restorer may restore the alias once.
    private bool Restore(NeuronId id) => Restorer.TryRestore(id.Brain, id.Type, CancellationToken.None).GetAwaiter().GetResult();

    // A generated source's alias is live: Restart forgets it.
    internal void MarkGenerated(string alias) => _generated[alias] = 0;

    internal void UnmarkGenerated(string alias) => _generated.TryRemove(alias, out _);

    // The synapse host and its restorer, sharing one loader; Restart replaces both, as a new process would. The compiler and its cache stay.
    private (DigitalBrain.Runtime.Synapses.SynapseHost Host, ISynapseRestorer Restorer) Runtime()
    {
        lock (_gate)
        {
            if (_runtime is { } runtime) { return runtime; }
            var loader = new DigitalBrain.Runtime.Synapses.SynapseLoader();
            var host = new DigitalBrain.Runtime.Synapses.SynapseHost(
                _compiler.Value,
                loader,
                Registrar,
                Source,
                brain => Get<IInstalls>(NeuronId.Parse($"{brain}/synapse.installs/default")),
                Store,
                Listeners,
                track: work => _ = Track(work));
            var restorer = Options.Services?.GetService(typeof(ISynapseRestorer)) as ISynapseRestorer
                ?? new DigitalBrain.Runtime.Synapses.SynapseRestorer(loader, Registrar, Store, Source, host);
            _runtime = (host, restorer);
            return (host, restorer);
        }
    }

    // The restart of a host: the generated aliases leave the registry, their (ended) hosts are dropped and the synapse host is replaced.
    // The old contexts are not unloaded: the journal keeps typed signals of their types, as a dead process would simply be gone.
    private async Task ForgetGenerated()
    {
        foreach (var alias in _generated.Keys.ToList())
        {
            _ = Unregister(alias);
            foreach (var host in _hosts.Values.Where(host => host.Id.Type == alias).ToList())
            {
                if (_hosts.TryRemove(new KeyValuePair<NeuronId, TestNeuronHost>(host.Id, host))) { host.Dispose(); }
            }
        }
        _generated.Clear();
        (DigitalBrain.Runtime.Synapses.SynapseHost Host, ISynapseRestorer Restorer)? old;
        lock (_gate)
        {
            old = _runtime;
            _runtime = null;
        }
        if (old is { } stopped) { await stopped.Host.DisposeAsync().ConfigureAwait(false); }
    }

    // Hosts alias as the state neuron of record from now on.
    internal void ReplaceStateType(string alias, Type record) => _types[alias] = typeof(StateNeuron<>).MakeGenericType(record);

    // Ends and forgets every host of alias, so the next call constructs the class registered then; state and synapses survive.
    internal async Task DropHosts(string alias)
    {
        foreach (var host in _hosts.Values.Where(host => host.Id.Type == alias).ToList())
        {
            await Track(host.Deactivate(DeactivationCause.Requested)).ConfigureAwait(false);
            if (_hosts.TryRemove(new KeyValuePair<NeuronId, TestNeuronHost>(host.Id, host))) { host.Dispose(); }
        }
    }

    private async Task RefreshGates(NeuronId publisher, GateChanged changed, CancellationToken ct)
    {
        var records = await Get<IInstalls>(new NeuronId(publisher.Brain, InstallsType, "default")).List(ct).ConfigureAwait(false);
        Gates.Refresh(publisher.Brain, records);
    }

    private const string InstallsType = "synapse.installs";

    private const string SourcesType = "synapse.sources";

    internal bool HasHost(NeuronId id) => _hosts.ContainsKey(id);

    private int _gateRestores;

    /// <summary>How many gate paths awaited the restore of an installed alias missing from the catalog; every other gate path runs inline.</summary>
    public int GateRestores => Volatile.Read(ref _gateRestores);

    internal void CountGateRestore() => Interlocked.Increment(ref _gateRestores);

    internal bool TryHost(NeuronId id, out TestNeuronHost host)
    {
        if (_hosts.TryGetValue(id, out host!)) { return true; }
        if (!_types.ContainsKey(id.Type)) { return false; }
        host = Host(id);
        return true;
    }

    internal T Proxy<T>(NeuronId caller, NeuronId target) where T : class
    {
        _ = Host(target);
        return NeuronProxy.Create<T>(target, new CallerSink(this, caller));
    }

    internal void Fail(Exception error) => _failures.Enqueue(error);

    // Counts the work toward Idle and records the errors nobody else will observe.
    internal Task Track(Task work) => TrackCore(work);

    internal Task<T> Track<T>(Task<T> work)
    {
        _ = TrackCore(work);
        return work;
    }

    private Task TrackCore(Task work)
    {
        lock (_gate)
        {
            if (_outstanding++ == 0) { _quiet = new(TaskCreationOptions.RunContinuationsAsynchronously); }
        }
        _ = work.ContinueWith(_ =>
        {
            lock (_gate)
            {
                if (--_outstanding == 0) { _quiet.TrySetResult(); }
            }
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return work;
    }

    private TestOS Register(Type type)
    {
        if (!typeof(Neuron).IsAssignableFrom(type) || type.IsAbstract) { throw new ArgumentException($"{type.FullName} is not a concrete neuron class.", nameof(type)); }
        var name = type.GetCustomAttribute<NeuronAttribute>(false)?.Type
            ?? throw new ArgumentException($"{type.FullName} has no [Neuron(\"type\")] attribute.", nameof(type));
        var registered = _types.GetOrAdd(name, type);
        if (registered != type) { throw new ArgumentException($"Neuron type '{name}' is already registered for {registered.FullName}.", nameof(type)); }
        _ = CallMarkers.Predicate(type);
        if (type.Assembly != typeof(Neuron).Assembly) { ScanModuleActions(type.Assembly); }
        return this;
    }

    private void ScanModuleActions(Assembly assembly)
    {
        if (!_scannedModules.TryAdd(assembly, 0)) { return; }
        foreach (var moduleType in assembly.GetTypes().Where(type => type is { IsAbstract: false, IsClass: true } && typeof(Module).IsAssignableFrom(type) && type.IsDefined(typeof(ModuleAttribute), false)))
        {
            try
            {
                foreach (var action in ModuleManifest.Of(moduleType).Actions)
                {
                    _ = _actions.TryAdd(action.Name, action);
                }
            }
            catch (Exception error) when (error is InvalidOperationException or InvalidDataException) { _ = error; }
        }
    }

    private static TaskCompletionSource Quiet()
    {
        var quiet = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        quiet.SetResult();
        return quiet;
    }

    // The proxy's sink: stamps the caller, then hands the call to the target's host.
    private sealed class CallerSink(TestOS os, NeuronId? caller) : IInvocationSink
    {
        public Task<JsonElement?> Invoke(NeuronId target, RuntimeInvocation call, CancellationToken cancellationToken)
            => os.Host(target).Invoke(Stamp(call), cancellationToken);

        public Task Post(NeuronId target, RuntimeInvocation call) => os.Host(target).Post(Stamp(call));

        public IAsyncEnumerable<JsonElement> Stream(NeuronId target, RuntimeInvocation call, CancellationToken cancellationToken)
            => os.Host(target).Stream(Stamp(call), cancellationToken);

        private RuntimeInvocation Stamp(RuntimeInvocation call) => call with { Behavior = caller?.ToString() ?? string.Empty };
    }

    private sealed class EmptyServices : IServiceProvider
    {
        internal static readonly EmptyServices Instance = new();

        public object? GetService(Type serviceType) => null;
    }
}
