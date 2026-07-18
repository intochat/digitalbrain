using System.Diagnostics;
using System.Reflection;
using System.Text.Json.Nodes;
using DigitalBrain.Core.Neurons;
using DigitalBrain.Runtime.Diagnostics;
using DigitalBrain.Runtime.Neurons.State;
using Orleans.Journaling;
using Orleans.Streams;
using Orleans.Streams.Core;

namespace DigitalBrain.Runtime.Neurons;

public abstract class Neuron
    : DurableGrain, IStreamSubscriptionObserver, IAsyncObserver<Synapse>, INeuron, ILifecycleParticipant<IGrainLifecycle>
{
    private IDurableList<Synapse>? _incoming;
    private IDurableList<Synapse>? _outgoing;
    private IGrainFactory? _grains;
    private ILogger? _logger;

    protected Neuron(
        IDurableList<Synapse> incoming,
        IDurableList<Synapse> outgoing,
        IGrainFactory grains,
        ILogger logger)
    {
        _incoming = incoming;
        _outgoing = outgoing;
        _grains = grains;
        _logger = logger;
    }

    protected Neuron()
    {
    }

    public virtual void Participate(IGrainLifecycle lifecycle)
    {
        lifecycle.Subscribe(
            this.GetType().FullName ?? this.GetType().Name,
            GrainLifecycleStage.SetupState,
            OnSetupState);
    }

    private Task OnSetupState(CancellationToken ct)
    {
        if (this.GrainContext is not null)
        {
            var services = this.GrainContext.ActivationServices;
            _grains ??= services.GetRequiredService<IGrainFactory>();
            _logger ??= services.GetRequiredService<ILoggerFactory>().CreateLogger(GetType());
            InjectDeclarativeState(services);
        }
        return Task.CompletedTask;
    }

    private void InjectDeclarativeState(IServiceProvider services)
    {
        var type = this.GetType();
        
        // Scan for fields with [State] or [NeuronState]
        var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        foreach (var field in fields)
        {
            if (field.IsDefined(typeof(StateAttribute), true) || field.IsDefined(typeof(NeuronStateAttribute), true))
            {
                var stateInstance = CreateStateInstance(field.FieldType, services);
                field.SetValue(this, stateInstance);
                BindStateToProperties(stateInstance);
            }
        }

        // Scan for properties with [State] or [NeuronState]
        var properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        foreach (var prop in properties)
        {
            if (prop.IsDefined(typeof(StateAttribute), true) || prop.IsDefined(typeof(NeuronStateAttribute), true))
            {
                if (prop.CanWrite)
                {
                    var stateInstance = CreateStateInstance(prop.PropertyType, services);
                    prop.SetValue(this, stateInstance);
                    BindStateToProperties(stateInstance);
                }
            }
        }
    }

    private object CreateStateInstance(Type stateType, IServiceProvider services)
    {
        var constructors = stateType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (constructors.Length == 0)
        {
            throw new InvalidOperationException($"State type {stateType.FullName} has no public or internal constructors.");
        }

        var ctor = constructors[0];
        var parameters = ctor.GetParameters();
        var args = new object?[parameters.Length];

        for (int i = 0; i < parameters.Length; i++)
        {
            var param = parameters[i];
            var paramType = param.ParameterType;

            // Resolve as a keyed service using the parameter name as the key, or standard service
            var key = param.Name ?? paramType.Name;
            
            // First try keyed service
            var resolved = services.GetKeyedService(paramType, key);
            
            // If not found, try standard service
            resolved ??= services.GetService(paramType);
            
            // Special fallback for time, logger, or factory
            if (resolved is null && paramType == typeof(TimeProvider))
            {
                resolved = services.GetService<TimeProvider>() ?? TimeProvider.System;
            }
            if (resolved is null && paramType.IsGenericType && paramType.GetGenericTypeDefinition() == typeof(ILogger<>))
            {
                var loggerFactory = services.GetService<ILoggerFactory>();
                if (loggerFactory is not null)
                {
                    var genericType = paramType.GetGenericArguments()[0];
                    resolved = typeof(LoggerFactoryExtensions)
                        .GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .First(m => m.Name == "CreateLogger" && m.IsGenericMethod)
                        .MakeGenericMethod(genericType)
                        .Invoke(null, new object[] { loggerFactory });
                }
            }

            args[i] = resolved ?? throw new InvalidOperationException($"Unable to resolve dependency of type {paramType.FullName} for state type {stateType.FullName}.");
        }

        return ctor.Invoke(args);
    }

    private void BindStateToProperties(object stateInstance)
    {
        // Use reflection to find "Incoming" and "Outgoing" properties on the state instance
        var stateType = stateInstance.GetType();
        
        var incomingProp = stateType.GetProperty("Incoming", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? stateType.GetProperty("incoming", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            
        var outgoingProp = stateType.GetProperty("Outgoing", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? stateType.GetProperty("outgoing", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        if (incomingProp is not null)
        {
            _incoming = incomingProp.GetValue(stateInstance) as IDurableList<Synapse>;
        }
        
        if (outgoingProp is not null)
        {
            _outgoing = outgoingProp.GetValue(stateInstance) as IDurableList<Synapse>;
        }
    }

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);
        InjectSettingProperties();

        try
        {
            var activated = new NeuronActivated(NeuronType, InstanceId.ToString());
            await FireSynapseAsync(activated, cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to fire NeuronActivated lifecycle synapse for {NeuronType}.", NeuronType);
        }
    }

    public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        try
        {
            var deactivated = new NeuronDeactivated(NeuronType, InstanceId.ToString());
            await FireSynapseAsync(deactivated, cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to fire NeuronDeactivated lifecycle synapse for {NeuronType}.", NeuronType);
        }

        await base.OnDeactivateAsync(reason, cancellationToken);
    }

    private void InjectSettingProperties()
    {
        var properties = GetType().GetProperties(
            BindingFlags.Public | 
            BindingFlags.NonPublic | 
            BindingFlags.Instance);

        var config = this.GrainContext?.ActivationServices?.GetService<IConfiguration>();

        foreach (var prop in properties)
        {
            var attr = prop.GetCustomAttribute<NeuronSettingAttribute>();
            if (attr is not null)
            {
                if (prop.PropertyType != typeof(string))
                {
                    throw new InvalidOperationException($"[NeuronSetting] can only be applied to string properties (property {prop.Name} on type {GetType().FullName} is not supported).");
                }

                if (!prop.CanWrite)
                {
                    throw new InvalidOperationException($"[NeuronSetting] property {prop.Name} on type {GetType().FullName} must have a setter.");
                }

                var resolvedValue = NeuronSettingResolver.Resolve(config, attr);
                prop.SetValue(this, resolvedValue);
            }
        }
    }

    public const int MaxJournalEntries = 500;
    public const string SynapseStreamProvider = "synapse";
    public const string GlobalTimelineNamespace = "digitalbrain.timeline";

    private static readonly Type? RfwCardType = typeof(Ui.RfwCard);
    private static readonly PropertyInfo? RfwCardHeadersProp = RfwCardType?.GetProperty("Headers");

    protected static readonly ActivitySource Activity = DigitalBrainTelemetry.Source;

    protected ILogger Logger => _logger ??= this.GrainContext.ActivationServices.GetRequiredService<ILoggerFactory>().CreateLogger(GetType());
    protected IGrainFactory Grains => _grains ??= this.GrainContext.ActivationServices.GetRequiredService<IGrainFactory>();

    protected IDurableList<Synapse> Incoming => _incoming ??= ResolveDurableList("incoming");
    protected IDurableList<Synapse> Outgoing => _outgoing ??= ResolveDurableList("outgoing");

    private IDurableList<Synapse> ResolveDurableList(string key)
    {
        if (this.GrainContext is null)
        {
            throw new InvalidOperationException("GrainContext is null");
        }

        var runtimeContextType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType("Orleans.Runtime.RuntimeContext"))
            .FirstOrDefault(t => t != null);

        if (runtimeContextType != null)
        {
            var setMethod = runtimeContextType.GetMethod("SetExecutionContext", 
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

            if (setMethod != null)
            {
                var args = new object?[] { this.GrainContext, null };
                setMethod.Invoke(null, args);
                var originalContext = args[1];

                try
                {
                    return this.GrainContext.ActivationServices.GetRequiredKeyedService<IDurableList<Synapse>>(key);
                }
                finally
                {
                    var restoreArgs = new object?[] { originalContext, null };
                    setMethod.Invoke(null, restoreArgs);
                }
            }
        }

        return this.GrainContext.ActivationServices.GetRequiredKeyedService<IDurableList<Synapse>>(key);
    }

    protected virtual string NeuronType => GetType().Name;
    protected Guid InstanceId
    {
        get
        {
            try
            {
                return this.GetPrimaryKey();
            }
            catch (InvalidOperationException)
            {
                var stringKey = this.GetPrimaryKeyString();
                var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(stringKey));
                return new Guid(hash[..16]);
            }
        }
    }

    protected async Task FireSynapseAsync(Synapse synapse, CancellationToken ct = default)
    {
        var instanceNeuronId = new NeuronId(InstanceId.ToString());
        var ambient = NeuronContext.Value;
        
        synapse = synapse.Stamp(instanceNeuronId, NeuronType, ambient);

        Console.WriteLine($"[DIAGNOSTIC] Neuron {NeuronType} (Instance {InstanceId}): Fired synapse {synapse.GetType().Name}. Caller={synapse.Headers.CallerNeuronType}/{synapse.Headers.CallerNeuronId.Value}, Receiver={synapse.Headers.ReceiverNeuronType}/{synapse.Headers.ReceiverNeuronId.Value}");

        Logger.LogInformation("Neuron {NeuronType} (Instance {InstanceId}): Fired synapse {SynapseType}. Headers: CorrelationId={CorrelationId}, Caller={CallerType}/{CallerId}, Receiver={ReceiverType}/{ReceiverId}",
            NeuronType, InstanceId, synapse.GetType().Name, synapse.Headers.CorrelationId.Value, synapse.Headers.CallerNeuronType, synapse.Headers.CallerNeuronId.Value, synapse.Headers.ReceiverNeuronType, synapse.Headers.ReceiverNeuronId.Value);

        using var activity = DigitalBrainTelemetry.StartSynapseActivity(
            DigitalBrainTelemetry.NeuronFire, synapse);
        activity?.SetTag(DigitalBrainTelemetry.TagNeuronType, NeuronType);
        activity?.SetTag(DigitalBrainTelemetry.TagCaller,
            $"{synapse.Headers.CallerNeuronType}/{synapse.Headers.CallerNeuronId.Value}");

        DigitalBrainTelemetry.CounterInstrument(DigitalBrainTelemetry.MetricSynapsesFired).Add(
            1,
            new(DigitalBrainTelemetry.TagNeuronType, NeuronType),
            new(DigitalBrainTelemetry.TagSynapseType, synapse.GetType().Name));

        Outgoing.Add(synapse);
        while (Outgoing.Count > MaxJournalEntries) Outgoing.RemoveAt(0);
        await WriteStateAsync(ct);

        synapse = DigitalBrainTelemetry.CaptureTraceContext(synapse);

        var receiverType = synapse.Headers.ReceiverNeuronType;
        if (receiverType == "External") receiverType = "GatewayNeuron";

        var streamProvider = this.GetStreamProvider(SynapseStreamProvider);
        var receiverStream = streamProvider.GetStream<Synapse>(
            StreamId.Create(receiverType, synapse.ReceiverNeuronId));
        await receiverStream.OnNextAsync(synapse);

        // Declarative [WireTo] auto-routing to sibling targets
        var wireToAttrs = GetType().GetCustomAttributes<WireToAttribute>(inherit: true);
        foreach (var attr in wireToAttrs)
        {
            var targetType = attr.Target;
            if (!string.IsNullOrEmpty(targetType))
            {
                var targetStream = streamProvider.GetStream<Synapse>(
                    StreamId.Create(targetType, synapse.ReceiverNeuronId != Guid.Empty ? synapse.ReceiverNeuronId : Guid.Empty));
                await targetStream.OnNextAsync(synapse);
            }
        }

        var timelineStream = streamProvider.GetStream<Synapse>(
            StreamId.Create(GlobalTimelineNamespace, Guid.Empty));
        await timelineStream.OnNextAsync(synapse);
    }

    public async Task OnNextAsync(Synapse item, StreamSequenceToken? token = null)
    {
        if (item is null) return;
        var itemId = item.Headers?.SynapseId.Value ?? Guid.Empty;
        Console.WriteLine($"[DIAGNOSTIC] Neuron {NeuronType} (Instance {InstanceId}): Received synapse {item.GetType().Name}. ID={itemId}, Caller={item.Headers?.CallerNeuronType}/{item.Headers?.CallerNeuronId.Value}, Receiver={item.Headers?.ReceiverNeuronType}/{item.Headers?.ReceiverNeuronId.Value}");
        if (Incoming.Any(s => (s.Headers?.SynapseId.Value ?? Guid.Empty) == itemId)) return;

        Incoming.Add(item);
        while (Incoming.Count > MaxJournalEntries) Incoming.RemoveAt(0);
        await WriteStateAsync();

        using var activity = DigitalBrainTelemetry.StartLinkedActivity(
            DigitalBrainTelemetry.NeuronHandle, item);
        activity?.SetTag(DigitalBrainTelemetry.TagNeuronType, NeuronType);

        var neuronTag = new KeyValuePair<string, object?>(
            DigitalBrainTelemetry.TagNeuronType, NeuronType);
        var synapseTag = new KeyValuePair<string, object?>(
            DigitalBrainTelemetry.TagSynapseType, item.GetType().Name);
        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            NeuronContext.Value = item;
            await HandleSynapseAsync(item);
        }
        catch
        {
            DigitalBrainTelemetry.CounterInstrument(DigitalBrainTelemetry.MetricNeuronErrors)
                .Add(1, neuronTag, synapseTag);
            throw;
        }
        finally
        {
            NeuronContext.Value = null;
            DigitalBrainTelemetry.CounterInstrument(DigitalBrainTelemetry.MetricSynapsesHandled)
                .Add(1, neuronTag, synapseTag);
            DigitalBrainTelemetry.HistogramInstrument(DigitalBrainTelemetry.MetricHandleDurationMs)
                .Record(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
                    neuronTag, synapseTag);
        }
    }

    public async Task OnSubscribed(IStreamSubscriptionHandleFactory handleFactory)
    {
        var handle = handleFactory.Create<Synapse>();
        try
        {
            await handle.ResumeAsync(this);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Resuming subscription with cached token failed. Falling back to fresh subscribe.");
            await handle.ResumeAsync(this, null);
        }
    }

    public Task OnCompletedAsync() => Task.CompletedTask;

    public Task OnErrorAsync(Exception ex)
    {
        if (ex is QueueCacheMissException || ex.GetType().FullName == "Orleans.Streams.QueueCacheMissException")
        {
            Logger.LogWarning(ex, "Transient stream cache miss in {Neuron}; Orleans pulling agent will recover.", NeuronType);
            return Task.CompletedTask;
        }
        DigitalBrainTelemetry.CounterInstrument(DigitalBrainTelemetry.MetricNeuronErrors).Add(
            1, new KeyValuePair<string, object?>(DigitalBrainTelemetry.TagNeuronType, NeuronType));
        Logger.LogError(ex, "Stream error in {Neuron}", NeuronType);
        return Task.CompletedTask;
    }

    protected virtual async Task HandleSynapseAsync(Synapse synapse)
    {
        await DispatchSynapseAsync(synapse);
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, Dictionary<Type, Func<Neuron, Synapse, CancellationToken, Task>>> DispatcherCache = new();

    protected async Task DispatchSynapseAsync(Synapse synapse)
    {
        var neuronType = GetType();
        var dispatchers = DispatcherCache.GetOrAdd(neuronType, type =>
        {
            var map = new Dictionary<Type, Func<Neuron, Synapse, CancellationToken, Task>>();
            var interfaces = type.GetInterfaces();
            foreach (var @interface in interfaces)
            {
                if (@interface.IsGenericType && @interface.GetGenericTypeDefinition() == typeof(IHandle<>))
                {
                    var synapseType = @interface.GetGenericArguments()[0];
                    var method = @interface.GetMethod(nameof(IHandle<Synapse>.HandleAsync), new[] { synapseType, typeof(CancellationToken) });
                    if (method != null)
                    {
                        var neuronParam = System.Linq.Expressions.Expression.Parameter(typeof(Neuron), "neuron");
                        var synapseParam = System.Linq.Expressions.Expression.Parameter(typeof(Synapse), "synapse");
                        var ctParam = System.Linq.Expressions.Expression.Parameter(typeof(CancellationToken), "ct");

                        var castNeuron = System.Linq.Expressions.Expression.Convert(neuronParam, type);
                        var castSynapse = System.Linq.Expressions.Expression.Convert(synapseParam, synapseType);

                        var call = System.Linq.Expressions.Expression.Call(castNeuron, method, castSynapse, ctParam);
                        var lambda = System.Linq.Expressions.Expression.Lambda<Func<Neuron, Synapse, CancellationToken, Task>>(
                            call, neuronParam, synapseParam, ctParam);

                        map[synapseType] = lambda.Compile();
                    }
                }
            }
            return map;
        });

        var synapseType = synapse.GetType();
        if (dispatchers.TryGetValue(synapseType, out var dispatcher))
        {
            await dispatcher(this, synapse, CancellationToken.None);
        }
        else
        {
            Logger.LogWarning("No handler registered for synapse type {SynapseType} on neuron {NeuronType}", synapseType.Name, NeuronType);
        }
    }

    protected Task RenderDefaultUiAsync(CancellationToken ct = default)
    {
        var initials = string.IsNullOrEmpty(NeuronType) ? "N" : NeuronType[..1].ToUpperInvariant();
        var tone = GetToneColorFromNamespace();
        
        var data = new JsonObject
        {
            ["title"] = NeuronType,
            ["subtitle"] = $"Neuron ID: {InstanceId.ToString()}",
            ["initials"] = initials,
            ["tone"] = tone
        };
        
        return RenderAsync("digitalbrain", "sample_neuron", data, ct);
    }

    private string GetToneColorFromNamespace()
    {
        var ns = GetType().Namespace ?? "";
        if (ns.Contains("Ai")) return "indigo";
        if (ns.Contains("Google")) return "amber";
        if (ns.Contains("Sqlite") || ns.Contains("Data")) return "teal";
        if (ns.Contains("Developer") || ns.Contains("INO")) return "purple";
        return "blue";
    }

    protected Task RenderAsync(string libraryName, string rootWidget, JsonObject data, CancellationToken ct = default)
    {
        if (RfwCardType is null)
            throw new InvalidOperationException("RfwCard type not found in DigitalBrain.Runtime.");

        var rfwCard = (Synapse)Activator.CreateInstance(RfwCardType,
            libraryName,             // LibraryName
            rootWidget,              // RootWidget
            data.ToJsonString()      // DataJson
        )!;

        if (RfwCardHeadersProp != null)
        {
            var metadata = SynapseMetadata.Create(
                synapseId: Guid.NewGuid(),
                correlationId: ResolveCorrelationId(),
                causationId: null,
                callerNeuronId: InstanceId,
                callerNeuronType: NeuronType,
                receiverNeuronId: Guid.Empty,
                receiverNeuronType: "HomeFeed",
                timestamp: DateTimeOffset.UtcNow
            );
            RfwCardHeadersProp.SetValue(rfwCard, metadata);
        }

        return FireSynapseAsync(rfwCard, ct);
    }

    protected DigitalBrainTelemetry.DeclaredCounter Counter(string name)
    {
        if (!DeclaredTelemetry().Counters.Contains(name))
            Logger.LogWarning(
                "Counter {Counter} is used by {Neuron} but not declared via " +
                "@telemetry:counter in its .feature; declare it spec-first.",
                name, NeuronType);
        return new DigitalBrainTelemetry.DeclaredCounter(DigitalBrainTelemetry.CounterInstrument(name));
    }

    protected DigitalBrainTelemetry.DeclaredHistogram Histogram(string name)
    {
        if (!DeclaredTelemetry().Histograms.Contains(name))
            Logger.LogWarning(
                "Histogram {Histogram} is used by {Neuron} but not declared via " +
                "@telemetry:histogram in its .feature; declare it spec-first.",
                name, NeuronType);
        return new DigitalBrainTelemetry.DeclaredHistogram(DigitalBrainTelemetry.HistogramInstrument(name));
    }

    static readonly System.Collections.Concurrent.ConcurrentDictionary<
        Type, (HashSet<string> Counters, HashSet<string> Histograms)> DeclaredCache = new();

    (HashSet<string> Counters, HashSet<string> Histograms) DeclaredTelemetry() =>
        DeclaredCache.GetOrAdd(GetType(), static type =>
        {
            const string counterTag = "@telemetry:counter:";
            const string histogramTag = "@telemetry:histogram:";
            var counters = new HashSet<string>(StringComparer.Ordinal);
            var histograms = new HashSet<string>(StringComparer.Ordinal);

            var suffix = "." + type.Name + ".feature";
            var resource = type.Assembly.GetManifestResourceNames()
                .FirstOrDefault(n =>
                    n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                    || n.Equals(type.Name + ".feature", StringComparison.OrdinalIgnoreCase));
            if (resource is null) return (counters, histograms);

            using var stream = type.Assembly.GetManifestResourceStream(resource);
            if (stream is null) return (counters, histograms);
            using var reader = new StreamReader(stream);

            foreach (var token in reader.ReadToEnd()
                         .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                if (token.StartsWith(counterTag, StringComparison.Ordinal))
                    counters.Add(token[counterTag.Length..]);
                else if (token.StartsWith(histogramTag, StringComparison.Ordinal))
                    histograms.Add(token[histogramTag.Length..]);
            }

            return (counters, histograms);
        });

    public Task<IReadOnlyList<Synapse>> GetIncomingJournalAsync(int fromIndex = 0, int toIndex = int.MaxValue)
        => Task.FromResult(Slice(Incoming, fromIndex, toIndex));

    public Task<IReadOnlyList<Synapse>> GetOutgoingJournalAsync(int fromIndex = 0, int toIndex = int.MaxValue)
        => Task.FromResult(Slice(Outgoing, fromIndex, toIndex));

    public Task<int> GetIncomingCountAsync() => Task.FromResult(Incoming.Count);
    public Task<int> GetOutgoingCountAsync() => Task.FromResult(Outgoing.Count);

    static IReadOnlyList<Synapse> Slice(IDurableList<Synapse> source, int from, int to)
    {
        var max = Math.Min(to, source.Count);
        if (from >= max) return Array.Empty<Synapse>();
        return source.Skip(from).Take(max - from).ToArray();
    }

    static CorrelationId ResolveCorrelationId()
    {
        var v = RequestContext.Get("DigitalBrain.CorrelationId");
        return v switch
        {
            Guid g => new CorrelationId(g),
            string s when Guid.TryParse(s, out var parsed) => new CorrelationId(parsed),
            _ => CorrelationId.New()
        };
    }
}