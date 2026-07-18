using System.Reflection;
using DigitalBrain.Runtime.Neurons;
using Microsoft.Extensions.Logging.Abstractions;
using Orleans.Journaling;
using Orleans.Streams;

namespace DigitalBrain.SDK;

public sealed record FiredSynapse(Synapse Synapse, string StreamNamespace)
{
    public string ReceiverType => StreamNamespace;
    public Guid ReceiverId => Synapse.ReceiverNeuronId;
    public string CallerType => Synapse.CallerNeuronType ?? "";
    public Guid CallerId => Synapse.CallerNeuronId;
    public Synapse Payload => Synapse;
}

public sealed class NeuronTestExecutionResult
{
    public IReadOnlyList<Synapse> IncomingJournal { get; }
    public IReadOnlyList<FiredSynapse> FiredSynapses { get; }

    public NeuronTestExecutionResult(IReadOnlyList<Synapse> incomingJournal, IReadOnlyList<FiredSynapse> firedSynapses)
    {
        IncomingJournal = incomingJournal;
        FiredSynapses = firedSynapses;
    }
}

public sealed class NeuronBuilder<T> where T : Neuron
{
    private readonly Dictionary<string, string> _settings = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _wireToTargets = new();
    private readonly ServiceCollection _services = new();
    private string _primaryKey = Guid.NewGuid().ToString();

    public NeuronBuilder()
    {
        // Register default mocks
        _services.AddSingleton<ILoggerFactory, NullLoggerFactory>();
        _services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        
        var mockGrainFactory = new MockGrainFactory();
        _services.AddSingleton<IGrainFactory>(mockGrainFactory);
        _services.AddSingleton<IGrainFactory>(mockGrainFactory);
        _services.AddSingleton<IStateMachineManager, MockStateMachineManager>();
    }

    public NeuronBuilder<T> WithSetting(string key, string value)
    {
        _settings[key] = value;
        return this;
    }

    public NeuronBuilder<T> WithWireTo(string target)
    {
        _wireToTargets.Add(target);
        return this;
    }

    public NeuronBuilder<T> WithPrimaryKey(string key)
    {
        _primaryKey = key;
        return this;
    }

    public NeuronBuilder<T> WithService<TService>(TService implementation) where TService : class
    {
        _services.AddSingleton(implementation);
        return this;
    }

    public NeuronBuilder<T> WithKeyedService<TService>(string key, TService implementation) where TService : class
    {
        _services.AddKeyedSingleton(key, implementation);
        return this;
    }

    public NeuronTestHarness<T> Build()
    {
        // 1. Build configuration
        var configDict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in _settings)
        {
            configDict[s.Key] = s.Value;
            configDict[$"DigitalBrain:Settings:{s.Key}"] = s.Value;
            configDict[$"Parameters:{s.Key}"] = s.Value;
        }
        var config = new ConfigurationBuilder().AddInMemoryCollection(configDict).Build();
        _services.AddSingleton<IConfiguration>(config);

        // 2. Setup incoming and outgoing durable lists
        var incomingJournal = new InMemoryDurableList<Synapse>();
        var outgoingJournal = new InMemoryDurableList<Synapse>();
        _services.AddKeyedSingleton<IDurableList<Synapse>>("incoming", incomingJournal);
        _services.AddKeyedSingleton<IDurableList<Synapse>>("outgoing", outgoingJournal);

        // 3. Setup stream provider to intercept fired synapses
        var firedSynapses = new List<FiredSynapse>();
        var mockStreamProvider = new MockStreamProvider((synapse, ns) =>
        {
            firedSynapses.Add(new FiredSynapse(synapse, ns));
        });
        _services.AddKeyedSingleton<IStreamProvider>("synapse", mockStreamProvider);

        // Ensure we can build T itself
        _services.AddTransient<T>();

        var serviceProvider = _services.BuildServiceProvider();

        // 4. Setup mock GrainContext using reflection to bypass Orleans runtime checks
        var neuronType = typeof(T).Name;
        var grainId = GrainId.Create(neuronType, _primaryKey);
        var mockGrainContext = new MockGrainContext(grainId, serviceProvider);

        T instance;
        using (SetRuntimeContext(mockGrainContext))
        {
            // Instantiate the concrete Neuron subclass T inside the active context so DurableGrain constructor gets services
            instance = ActivatorUtilities.CreateInstance<T>(serviceProvider);

            var grainContextProp = typeof(Grain).GetProperty("GrainContext", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (grainContextProp != null)
            {
                grainContextProp.SetValue(instance, mockGrainContext);
            }

            // Apply WireTo attributes dynamically by injecting target types to MockStreamProvider if needed
            // For static [WireTo] attributes, Neuron.cs reads them at runtime and fires them to the provider, which our MockStreamProvider captures.

            // 5. Invoke OnActivateAsync using reflection inside active context
            var onActivateMethod = typeof(T).GetMethod("OnActivateAsync", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (onActivateMethod != null)
            {
                var task = (Task)onActivateMethod.Invoke(instance, new object[] { CancellationToken.None })!;
                task.GetAwaiter().GetResult();
            }
        }

        return new NeuronTestHarness<T>(instance, incomingJournal, outgoingJournal, firedSynapses);
    }

    private static IDisposable SetRuntimeContext(IGrainContext context)
    {
        var runtimeContextType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType("Orleans.Runtime.RuntimeContext"))
            .FirstOrDefault(t => t != null);

        if (runtimeContextType == null)
        {
            return new RuntimeDisposable(() => {});
        }

        var setMethod = runtimeContextType.GetMethod("SetExecutionContext", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (setMethod == null)
        {
            return new RuntimeDisposable(() => {});
        }

        var args = new object?[] { context, null };
        setMethod.Invoke(null, args);
        var originalContext = args[1];

        return new RuntimeDisposable(() =>
        {
            var restoreArgs = new object?[] { originalContext, null };
            setMethod.Invoke(null, restoreArgs);
        });
    }

    private class RuntimeDisposable(Action action) : IDisposable
    {
        public void Dispose() => action();
    }
}

public sealed class NeuronTestHarness<T> where T : Neuron
{
    public T Instance { get; }
    private readonly InMemoryDurableList<Synapse> _incoming;
    private readonly InMemoryDurableList<Synapse> _outgoing;
    private readonly List<FiredSynapse> _firedSynapses;

    public NeuronTestHarness(T instance, InMemoryDurableList<Synapse> incoming, InMemoryDurableList<Synapse> outgoing, List<FiredSynapse> firedSynapses)
    {
        Instance = instance;
        _incoming = incoming;
        _outgoing = outgoing;
        _firedSynapses = firedSynapses;
    }

    public async Task<NeuronTestExecutionResult> TestReceiveAsync(Synapse synapse, CancellationToken ct = default)
    {
        // Setup ambient context
        var oldContext = NeuronContext.Value;
        NeuronContext.Value = synapse;
        try
        {
            // Call OnNextAsync to invoke the neuron's handling flow
            await Instance.OnNextAsync(synapse);
        }
        finally
        {
            NeuronContext.Value = oldContext;
        }

        return new NeuronTestExecutionResult(_incoming, _firedSynapses);
    }
}

// DurableList, DurableDictionary, DurableValue mock implementations
public class InMemoryDurableList<T> : List<T>, IDurableList<T>
{
    public new void AddRange(IEnumerable<T> items) => base.AddRange(items);
    public new System.Collections.ObjectModel.ReadOnlyCollection<T> AsReadOnly() => base.AsReadOnly();
}

public class InMemoryDurableDictionary<TKey, TValue> : Dictionary<TKey, TValue>, IDurableDictionary<TKey, TValue>
    where TKey : notnull
{
}

public class InMemoryDurableValue<T> : IDurableValue<T>
{
    public T? Value { get; set; }
}

public class MockStateMachineManager : IStateMachineManager
{
    private readonly Dictionary<string, IDurableStateMachine> _stateMachines = new(StringComparer.Ordinal);

    public ValueTask DeleteStateAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
    public ValueTask InitializeAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public void RegisterStateMachine(string name, IDurableStateMachine stateMachine)
    {
        _stateMachines[name] = stateMachine;
    }

    bool IStateMachineManager.TryGetStateMachine(string name, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IDurableStateMachine? stateMachine)
    {
        if (_stateMachines.TryGetValue(name, out var sm))
        {
            stateMachine = sm;
            return true;
        }
        stateMachine = null;
        return false;
    }

    public ValueTask WriteStateAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
}

// Orleans Mock Contexts and lifecycle classes
public class MockGrainContext : IGrainContext
{
    public MockGrainContext(GrainId grainId, IServiceProvider services)
    {
        GrainId = grainId;
        ActivationServices = services;
        ObservableLifecycle = new MockGrainLifecycle();
    }

    public GrainId GrainId { get; }
    public IServiceProvider ActivationServices { get; }
    public IGrainLifecycle ObservableLifecycle { get; }

    public ActivationId ActivationId => ActivationId.NewId();
    public GrainAddress Address => null!;
    public Task Deactivated => Task.CompletedTask;
    public object? GrainInstance => null;
    public GrainReference GrainReference => null!;
    public IWorkItemScheduler Scheduler => null!;

    public void Activate(Dictionary<string, object>? components, CancellationToken ct) { }
    public void Deactivate(DeactivationReason reason, CancellationToken ct) { }
    public void Migrate(Dictionary<string, object>? components, CancellationToken ct) { }
    public void ReceiveMessage(object message) { }
    public void Rehydrate(IRehydrationContext context) { }
    public void SetComponent<TComponent>(TComponent? component) where TComponent : class { }

    public object? GetTarget() => null;
    public object? GetComponent(Type componentType) => null;
    public TComponent? GetComponent<TComponent>() where TComponent : class => null;
    public bool Equals(IGrainContext? other) => other != null && other.GrainId == GrainId;
}

public class MockGrainLifecycle : IGrainLifecycle
{
    public IDisposable Subscribe(string name, int stage, ILifecycleObserver observer)
    {
        return new DisposableAction(() => {});
    }

    public void AddMigrationParticipant(IGrainMigrationParticipant participant) {}
    public void RemoveMigrationParticipant(IGrainMigrationParticipant participant) {}

    private class DisposableAction(Action action) : IDisposable
    {
        public void Dispose() => action();
    }
}

public class MockGrainFactory : IGrainFactory
{
    public TGrainInterface GetGrain<TGrainInterface>(Guid primaryKey, string? grainClassNamePrefix = null) where TGrainInterface : IGrainWithGuidKey
    {
        return default!;
    }

    public TGrainInterface GetGrain<TGrainInterface>(long primaryKey, string? grainClassNamePrefix = null) where TGrainInterface : IGrainWithIntegerKey
    {
        return default!;
    }

    public TGrainInterface GetGrain<TGrainInterface>(string primaryKey, string? grainClassNamePrefix = null) where TGrainInterface : IGrainWithStringKey
    {
        return default!;
    }

    public TGrainInterface GetGrain<TGrainInterface>(Guid primaryKey, string keyExtension, string? grainClassNamePrefix = null) where TGrainInterface : IGrainWithGuidCompoundKey
    {
        return default!;
    }

    public TGrainInterface GetGrain<TGrainInterface>(long primaryKey, string keyExtension, string? grainClassNamePrefix = null) where TGrainInterface : IGrainWithIntegerCompoundKey
    {
        return default!;
    }

    public IAddressable GetGrain(GrainId grainId) => null!;
    public TGrainInterface GetGrain<TGrainInterface>(GrainId grainId) where TGrainInterface : IAddressable => default!;

    public IGrain GetGrain(Type grainInterfaceType, Guid grainPrimaryKey) => null!;
    public IGrain GetGrain(Type grainInterfaceType, long grainPrimaryKey) => null!;
    public IGrain GetGrain(Type grainInterfaceType, string grainPrimaryKey) => null!;

    public IGrain GetGrain(Type grainInterfaceType, Guid grainPrimaryKey, string keyExtension) => null!;
    public IGrain GetGrain(Type grainInterfaceType, long grainPrimaryKey, string keyExtension) => null!;

    public IAddressable GetGrain(GrainId grainId, GrainInterfaceType interfaceType) => null!;
    public IAddressable GetGrain(Type grainInterfaceType, IdSpan grainKey, string keyExtension) => null!;
    public IAddressable GetGrain(Type grainInterfaceType, IdSpan grainKey) => null!;
    public TGrainObserverInterface CreateObjectReference<TGrainObserverInterface>(IGrainObserver obj) where TGrainObserverInterface : IGrainObserver => default!;
    public void DeleteObjectReference<TGrainObserverInterface>(IGrainObserver obj) where TGrainObserverInterface : IGrainObserver { }
}

public class MockStreamProvider : IStreamProvider
{
    private readonly Action<Synapse, string> _onFired;

    public MockStreamProvider(Action<Synapse, string> onFired)
    {
        _onFired = onFired;
    }

    public string Name => "synapse";
    public bool IsRewindable => false;

    public IAsyncStream<T> GetStream<T>(StreamId streamId)
    {
        return new MockAsyncStream<T>((synapse) => _onFired(synapse, streamId.GetNamespace() ?? ""));
    }
}

public class MockAsyncStream<T> : IAsyncStream<T>
{
    private readonly Action<Synapse> _onFired;

    public MockAsyncStream(Action<Synapse> onFired)
    {
        _onFired = onFired;
    }

    public StreamId StreamId => default;
    public string ProviderName => "synapse";

    public int CompareTo(IAsyncStream<T>? other) => 0;
    public bool Equals(IAsyncStream<T>? other) => true;

    public Task OnNextAsync(T item, StreamSequenceToken? token = null)
    {
        if (item is Synapse synapse)
        {
            _onFired(synapse);
        }
        return Task.CompletedTask;
    }

    public Task OnCompletedAsync() => Task.CompletedTask;
    public Task OnErrorAsync(Exception ex) => Task.CompletedTask;

    public Task<IList<StreamSubscriptionHandle<T>>> GetAllSubscriptionHandles()
    {
        return Task.FromResult<IList<StreamSubscriptionHandle<T>>>(new List<StreamSubscriptionHandle<T>>());
    }

    public Task OnNextBatchAsync(IEnumerable<T> batch, StreamSequenceToken? token = null) => Task.CompletedTask;

    public Task<StreamSubscriptionHandle<T>> SubscribeAsync(IAsyncObserver<T> observer) => null!;
    public Task<StreamSubscriptionHandle<T>> SubscribeAsync(IAsyncObserver<T> observer, StreamSequenceToken? token, string? filterData = null) => null!;

    public bool IsRewindable => false;
    public Task<StreamSubscriptionHandle<T>> SubscribeAsync(IAsyncBatchObserver<T> observer) => null!;
    public Task<StreamSubscriptionHandle<T>> SubscribeAsync(IAsyncBatchObserver<T> observer, StreamSequenceToken? token) => null!;
}
