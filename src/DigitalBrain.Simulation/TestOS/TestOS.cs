using DigitalBrain.Limits;
using DigitalBrain.Modules;
using DigitalBrain.Scheduling;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DigitalBrain.Access;
using DigitalBrain.Contracts;
using DigitalBrain.Invocation;
using DigitalBrain.Kernel;
using DigitalBrain.Neurons;
using DigitalBrain.Time;
using DigitalBrain.Units;

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
    private readonly ConcurrentDictionary<string, ActionDescriptor> _actions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Assembly, byte> _scannedModules = new();
    private readonly ConcurrentDictionary<string, Type> _signalTypes = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<(BrainId Brain, string SourceKey), IReadOnlyList<string>> _sourceAliases = new();

    internal IReadOnlyList<BrainId> AllBrains => [.. _hosts.Keys.Select(h => h.Brain).Union(_journals.Keys).Distinct()];

    internal Type? TypeOf(string alias)
    {
        if (_signalTypes.TryGetValue(alias, out var type)) { return type; }
        foreach (var desc in Source.Static.Synapses)
        {
            if (desc.Signal is { } signalType && SignalAliases.Of(signalType) == alias)
            {
                _signalTypes[alias] = signalType;
                return signalType;
            }
        }
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic || assembly.IsCollectible) { continue; }
            try
            {
                foreach (var t in assembly.GetTypes())
                {
                    if (typeof(Signal).IsAssignableFrom(t) && !t.IsAbstract && SignalAliases.Of(t) == alias)
                    {
                        if (!t.IsCollectible) { _signalTypes[alias] = t; }
                        return t;
                    }
                }
            }
            catch { }
        }
        foreach (var desc in Catalog.Synapses)
        {
            if (desc.Signal is { } signalType && SignalAliases.Of(signalType) == alias)
            {
                return signalType;
            }
        }
        return null;
    }

    private TestOS(TestOSOptions options)
    {
        Options = options;
        Clock = new TestClock(options.Start);
        Capabilities = new CapabilityFakes();
        Loader = new DigitalBrain.Runtime.Synapses.UnitLoader();
        LiveBindings = new LiveBindings();
        Source = new SynapseCatalogSource(SynapseCatalog.Scan(), () => Loader);
        Resolver = new UnitResolver(
            Loader,
            Source,
            LiveBindings,
            id => Get<ISources>(id),
            id => Get<IUnitDeployment>(id));
        Synapses = new DigitalBrain.Runtime.Synapses.UnitDeployer(
            _compiler.Value,
            Loader,
            id => Get<ISources>(id),
            id => Get<IUnitDeployment>(id));
        Services = new TestOSServices(Capabilities, options.Services ?? EmptyServices.Instance, new TestInstallHooks(this), new TestSlotResolver(this), Source, () => [.. _actions.Values], () => Loader, () => Resolver, () => LiveBindings, () => _compiler.Value, () => Synapses);
        Delivery = new SynapseRegistry(this);
        Confirmations = new TestConfirmations(Clock);
        Listeners = new TestClusterListeners(this);
        AccessOptions = new AccessOptions { UnstampedEdgeIsKernel = true, AuditContractInternal = true };
        AccessCounters = new AccessCounters();
        Access = new AccessGate(TestCallerAmbient.Instance, AccessOptions, AccessCounters);
        Edge = new EdgeAuthority(TestCallerAmbient.Instance);
        Register<InstallsNeuron>();
        Register<SourcesNeuron>();
        Register<PoliciesNeuron>();
        Register<TimeNeuron>();
        Register<DigitalBrain.Operations.OperationNeuron>();
        Register<DigitalBrain.Confirmation.ConfirmationsNeuron>();
        Register<UnitDeployment>();
        Register<AsksNeuron>();
        Register<AskBatchNeuron>();

        _ = Listeners.Listen<Deployed>("testos.live.catalog", async (pub, s, ct) =>
        {
            _sourceAliases[(pub.Brain, s.SourceKey)] = s.Aliases;
            LiveBindings.Set(pub.Brain, new BuildRef(s.SourceKey, s.Hash));
            foreach (var alias in s.Aliases)
            {
                await DropHosts(pub.Brain, alias).ConfigureAwait(false);
            }
        });

        _ = Listeners.Listen<Undeployed>("testos.live.catalog", async (pub, s, ct) =>
        {
            LiveBindings.Retire(pub.Brain, s.SourceKey);
            if (_sourceAliases.TryRemove((pub.Brain, s.SourceKey), out var aliases))
            {
                foreach (var alias in aliases)
                {
                    await DropHosts(pub.Brain, alias).ConfigureAwait(false);
                }
            }
        });
    }

    /// <summary>The <c>[State]</c> records and synapse functions in force now: <see cref="Source"/>'s static catalog plus live unit catalogs.</summary>
    public SynapseCatalog Catalog
    {
        get
        {
            var cat = Source.Static;
            foreach (var brain in AllBrains)
            {
                try
                {
                    var brainCatalogs = Resolver.Catalogs(brain).GetAwaiter().GetResult();
                    foreach (var c in brainCatalogs)
                    {
                        if (!ReferenceEquals(c, Source.Static))
                        {
                            cat = cat.Merge(c);
                        }
                    }
                }
                catch { }
            }
            return cat;
        }
    }

    /// <summary>
    /// The live catalog every state neuron, the installs neuron and <see cref="Gates"/> read on each turn or call, so a change reaches
    /// neurons activated before it. <see cref="Register(Assembly)"/> merges into it and <see cref="Unregister(string)"/> removes from it.
    /// </summary>
    public SynapseCatalogSource Source { get; }

    /// <summary>The unit loader managing collectible unit contexts on this TestOS.</summary>
    public IUnitLoader Loader { get; private set; }

    /// <summary>The unit resolver mapping (brain, alias) to executable unit handles.</summary>
    public UnitResolver Resolver { get; private set; }

    /// <summary>Silo-local live bindings hint.</summary>
    public LiveBindings LiveBindings { get; private set; }

    /// <summary>
    /// The synapse host of this TestOS: compiles and deploys generated sources.
    /// </summary>
    public ISynapseHost Synapses { get; private set; }

    /// <summary>Host-level listeners on the signals of every brain of this TestOS (04-19); they survive <see cref="Restart"/>.</summary>
    public DigitalBrain.Kernel.IClusterListeners ClusterListeners => Listeners;

    /// <summary>The edge minting authority of this TestOS.</summary>
    public EdgeAuthority Edge { get; }

    /// <summary>The access admission gate of this TestOS.</summary>
    internal AccessGate Access { get; }

    /// <summary>The operational access configuration of this TestOS.</summary>
    public AccessOptions AccessOptions { get; }

    /// <summary>Observability counters for access telemetry.</summary>
    public AccessCounters AccessCounters { get; }

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
    /// functions into <see cref="Catalog"/>, and hosts each state alias as a <see cref="UnitNeuron{TState}"/>.
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
            var neuron = typeof(UnitNeuron<>).MakeGenericType(record);
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
        if (_types.TryGetValue(alias, out var type) && type.IsGenericType && type.GetGenericTypeDefinition() == typeof(UnitNeuron<>))
        {
            _ = _types.TryRemove(new KeyValuePair<string, Type>(alias, type));
            foreach (var host in _hosts.Values.Where(host => host.Id.Type == alias).ToList())
            {
                if (_hosts.TryRemove(new KeyValuePair<NeuronId, TestNeuronHost>(host.Id, host)))
                {
                    Delivery.RemoveSubscriber(host.Id);
                    host.Dispose();
                }
            }
        }
        return this;
    }

    /// <summary>A typed proxy to <paramref name="id"/>. Calls go through <see cref="NeuronProxy"/> and <see cref="NeuronBinder"/> as JSON.</summary>
    /// <exception cref="InvalidOperationException">No class is registered for the id's type.</exception>
    public T Get<T>(NeuronId id) where T : class
    {
        if (id.Type == DigitalBrain.Confirmation.ConfirmationsNeuron.NeuronType)
        {
            if (typeof(T) == typeof(DigitalBrain.Confirmation.IConfirmationRequests)) { return (T)(object)new BrainConfirmationRequests(Confirmations, this, id.Brain); }
            if (typeof(T) == typeof(DigitalBrain.Confirmation.IConfirmationInbox)) { return (T)(object)Confirmations; }
        }
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

    /// <summary>
    /// Seeds the raw stored state of <paramref name="id"/>, as a store written by an earlier version would hold it: the neuron loads it on
    /// its next activation (seed before it activates, or after <see cref="Restart"/>). No signal is journaled.
    /// </summary>
    /// <param name="id">The neuron.</param>
    /// <param name="json">The stored state as JSON, in the wire shape the runtime saves (camelCase).</param>
    public void SeedStoredState(NeuronId id, string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var document = JsonDocument.Parse(json);
        Journal(id.Brain).Seed(id, document.RootElement);
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
        foreach (var host in _hosts.Values.ToList())
        {
            if (_hosts.TryRemove(new KeyValuePair<NeuronId, TestNeuronHost>(host.Id, host)))
            {
                Delivery.RemoveSubscriber(host.Id);
                host.Dispose();
            }
        }
        _signalTypes.Clear();
        _sourceAliases.Clear();

        Loader = new DigitalBrain.Runtime.Synapses.UnitLoader();
        LiveBindings = new LiveBindings();
        Resolver = new UnitResolver(
            Loader,
            Source,
            LiveBindings,
            id => Get<ISources>(id),
            id => Get<IUnitDeployment>(id));
        Synapses = new DigitalBrain.Runtime.Synapses.UnitDeployer(
            _compiler.Value,
            Loader,
            id => Get<ISources>(id),
            id => Get<IUnitDeployment>(id));

        foreach (var subscriber in Delivery.Behind())
        {
            if (TryHost(subscriber, out var host))
            {
                await Track(host.Activate()).ConfigureAwait(false);
            }
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
        UnitHandle? handle = null;
        if (!_types.TryGetValue(id.Type, out var type))
        {
            handle = Resolver.Resolve(id.Brain, id.Type, UnitView.Live, CancellationToken.None).GetAwaiter().GetResult();
            if (handle is not null)
            {
                type = typeof(UnitNeuron<>).MakeGenericType(handle.StateOf(id.Type));
            }
            else
            {
                throw new InvalidOperationException($"No neuron class is registered for type '{id.Type}'. Call Register<TNeuron>() first.");
            }
        }
        var created = new TestNeuronHost(this, id, type, handle);
        var added = _hosts.GetOrAdd(id, created);
        if (!ReferenceEquals(added, created)) { created.Dispose(); }
        return added;
    }

    // Hosts alias as the unit neuron of record from now on.
    internal void ReplaceStateType(string alias, Type record) => _types[alias] = typeof(UnitNeuron<>).MakeGenericType(record);

    // Ends and forgets every host of alias in brain, so the next call constructs the class registered then; state and synapses survive.
    internal async Task DropHosts(BrainId brain, string alias)
    {
        foreach (var host in _hosts.Values.Where(host => host.Id.Brain == brain && host.Id.Type == alias).ToList())
        {
            await Track(host.Deactivate(DeactivationCause.Requested)).ConfigureAwait(false);
            if (_hosts.TryRemove(new KeyValuePair<NeuronId, TestNeuronHost>(host.Id, host)))
            {
                host.Dispose();
            }
        }
    }

    private const string InstallsType = "synapse.installs";

    private const string SourcesType = "synapse.sources";

    internal bool HasHost(NeuronId id) => _hosts.ContainsKey(id);

    internal bool TryHost(NeuronId id, out TestNeuronHost host)
    {
        if (_hosts.TryGetValue(id, out host!)) { return true; }
        try
        {
            host = Host(id);
            return true;
        }
        catch (InvalidOperationException)
        {
            host = null!;
            return false;
        }
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
        foreach (var moduleType in assembly.GetTypes().Where(type => type is { IsAbstract: false, IsClass: true } && typeof(DigitalBrain.Modules.Module).IsAssignableFrom(type) && type.IsDefined(typeof(ModuleAttribute), false)))
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
        {
            using var frame = Hop(target);
            return os.Host(target).Invoke(Stamp(call), cancellationToken);
        }

        public Task Post(NeuronId target, RuntimeInvocation call)
        {
            using var frame = Hop(target);
            return os.Host(target).Post(Stamp(call));
        }

        public IAsyncEnumerable<JsonElement> Stream(NeuronId target, RuntimeInvocation call, CancellationToken cancellationToken)
            => StreamInternal(target, call, cancellationToken);

        private async IAsyncEnumerable<JsonElement> StreamInternal(NeuronId target, RuntimeInvocation call, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            using var frame = Hop(target);
            await foreach (var item in os.Host(target).Stream(Stamp(call), cancellationToken).ConfigureAwait(false))
            {
                yield return item;
            }
        }

        private IDisposable Hop(NeuronId target)
        {
            var current = TestCallerAmbient.Instance.Current;
            return TestCallerAmbient.Instance.Enter(current?.Hop(via: caller, inside: caller is not null));
        }

        private RuntimeInvocation Stamp(RuntimeInvocation call) => call with { Behavior = caller?.ToString() ?? string.Empty };
    }

    private sealed class EmptyServices : IServiceProvider
    {
        internal static readonly EmptyServices Instance = new();

        public object? GetService(Type serviceType) => null;
    }
}
