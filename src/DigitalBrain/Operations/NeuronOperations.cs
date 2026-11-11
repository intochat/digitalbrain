using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using DigitalBrain.Contracts;
using DigitalBrain.Invocation;
using DigitalBrain.Neurons;

namespace DigitalBrain.Operations;

internal static class OperationPlanRegistry
{
    private static readonly ConcurrentDictionary<string, OperationPlan> Plans = new();

    public static void Register(string id, OperationPlan plan)
    {
        Plans[id] = plan;
    }

    public static OperationPlan? Take(string id)
    {
        Plans.TryRemove(id, out var plan);
        return plan;
    }

    public static void Remove(string id)
    {
        Plans.TryRemove(id, out _);
    }
}

/// <summary>Operation handle for in-process tracking of progress, cancel, and completion.</summary>
public sealed class OperationHandle : IOperation, IDisposable
{
    private readonly CancellationTokenSource _cancellation = new();
    private readonly TaskCompletionSource<OperationResult> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IOperationNeuron _neuron;
    private readonly TimeProvider _time;
    private readonly OperationRegistry _registry;
    private readonly bool _driven;
    private DateTimeOffset _lastReport = DateTimeOffset.MinValue;
    private int _completing;

    internal OperationHandle(OperationId id, BrainId brain, IOperationNeuron neuron, TimeProvider time, OperationRegistry registry, bool driven)
    {
        Id = id;
        Brain = brain;
        _neuron = neuron;
        _time = time;
        _registry = registry;
        _driven = driven;
        if (driven)
        {
            _ = MonitorAsync();
        }
    }

    private async Task MonitorAsync()
    {
        try
        {
            while (!_done.Task.IsCompleted && !_cancellation.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), _time).ConfigureAwait(false);
                var state = await _neuron.Get().ConfigureAwait(false);
                if (state is { Phase: OperationPhase.Done, Result: { } result })
                {
                    NotifyDone(result);
                    break;
                }
            }
        }
        catch
        {
            // Best effort
        }
    }

    /// <inheritdoc />
    public OperationId Id { get; }

    /// <summary>The brain the operation runs in.</summary>
    public BrainId Brain { get; }

    /// <inheritdoc />
    public CancellationToken Cancellation => _cancellation.Token;

    /// <summary>Completes with the result once the operation ended.</summary>
    public Task<OperationResult> Completion => _done.Task;

    /// <inheritdoc />
    public async ValueTask Report(double fraction)
    {
        if (_cancellation.IsCancellationRequested || Volatile.Read(ref _completing) != 0)
        {
            return;
        }

        var now = _time.GetUtcNow();
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_cancellation.IsCancellationRequested || (fraction < 1 && now - _lastReport < TimeSpan.FromMilliseconds(250)))
            {
                return;
            }

            _lastReport = now;
            if (!await _neuron.Progress(fraction).ConfigureAwait(false))
            {
                await _cancellation.CancelAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask Complete(OperationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (Interlocked.Exchange(ref _completing, 1) != 0)
        {
            return;
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var saved = await _neuron.Complete(result).ConfigureAwait(false);
            _done.TrySetResult(saved.Result ?? result);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            _done.TrySetException(error);
            throw;
        }
        finally
        {
            _gate.Release();
            _registry.Remove(Id);
        }
    }

    internal void NotifyDone(OperationResult result)
    {
        _done.TrySetResult(result);
        _registry.Remove(Id);
    }

    internal async Task RequestCancel()
    {
        await _cancellation.CancelAsync().ConfigureAwait(false);
        if (!_driven)
        {
            await Complete(new OperationResult.Cancelled()).ConfigureAwait(false);
        }
    }

    internal void Fault(Exception error)
    {
        _done.TrySetException(error);
        _registry.Remove(Id);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _cancellation.Dispose();
        _gate.Dispose();
    }
}

/// <summary>Registry of active local operations on this node.</summary>
public sealed class OperationRegistry
{
    private readonly ConcurrentDictionary<OperationId, OperationHandle> _running = new();

    /// <summary>Adds a handle to the local registry.</summary>
    public void Add(OperationHandle operation) => _running[operation.Id] = operation;

    /// <summary>Tries to get a handle for an operation id.</summary>
    public bool TryGet(OperationId id, out OperationHandle operation) => _running.TryGetValue(id, out operation!);

    /// <summary>Removes an operation from the registry.</summary>
    public void Remove(OperationId id) => _running.TryRemove(id, out _);
}

/// <summary>Operations query implementation reading from operation neurons.</summary>
public sealed class NeuronOperationsQuery(IServiceProvider services, BrainId brain) : IOperationsQuery
{
    /// <inheritdoc />
    public async Task<OperationView?> Read(OperationId id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var neuron = ResolveNeuron(services, brain, id);
        var state = await neuron.Get().ConfigureAwait(false);
        return state is null
            ? null
            : new OperationView(new(state.Id), state.Kind, state.Origin, state.Fraction, state.Result, state.Started, state.TraceId, state.Phase, state.ApprovalPlanId);
    }

    internal static IOperationNeuron ResolveNeuron(IServiceProvider services, BrainId brain, OperationId id)
    {
        var proxies = services.GetService(typeof(INeuronProxies)) as INeuronProxies;
        return proxies?.Get<IOperationNeuron>(new NeuronId(brain, OperationNeuron.NeuronType, id.Value))
            ?? throw new InvalidOperationException("No INeuronProxies registered to resolve IOperationNeuron.");
    }
}

/// <summary>Neuron-backed operations service implementing IOperations and IOperationsQuery.</summary>
public sealed class NeuronOperations(IServiceProvider services, BrainId brain, string origin) : IOperations, IOperationsQuery, IOperationReconciliation
{
    private OperationRegistry Registry => (services.GetService(typeof(OperationRegistry)) as OperationRegistry)
        ?? throw new InvalidOperationException("OperationRegistry is required in services.");

    /// <inheritdoc />
    public async Task<IOperation> Start(OperationPlan plan, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var time = (services.GetService(typeof(TimeProvider)) as TimeProvider) ?? TimeProvider.System;
        var now = time.GetUtcNow();
        var id = new OperationId("op-" + Guid.NewGuid().ToString("N"));

        long? length = null;
        string? hash = null;
        if (plan.Payload is { } payload)
        {
            length = payload.Length;
            hash = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(payload.Span));
        }

        var json = JsonSerializer.Serialize(plan.Persisted(), plan.GetType(), RuntimeProtocol.Json);
        var driverLifetime = (services.GetService(typeof(IOperationDriverLifetime)) as IOperationDriverLifetime)?.Capture();
        var driver = CaptureDriver(services);
        var traceId = CaptureTraceId(services);

        var initial = new OperationState(
            id.Value,
            brain.Value,
            origin,
            plan.GetType().Name,
            json,
            length,
            hash,
            OperationPhase.Running,
            false,
            0d,
            null,
            now,
            now,
            Driver: driver,
            DriverLifetime: driverLifetime,
            TraceId: traceId);

        var neuron = NeuronOperationsQuery.ResolveNeuron(services, brain, id);
        await neuron.Begin(initial).ConfigureAwait(false);

        OperationPlanRegistry.Register(id.Value, plan);

        var handlers = services.GetService(typeof(OperationHandlers)) as OperationHandlers;
        var execution = handlers?.Find(plan.GetType());
        var handle = new OperationHandle(id, brain, neuron, time, Registry, driven: execution is not null);
        Registry.Add(handle);

        var scope = new OperationScope(brain, origin, services, null, this);
        var approval = execution is not null ? await execution.Describe(plan, scope, ct).ConfigureAwait(false) : null;
        await neuron.Prepared(approval).ConfigureAwait(false);

        return handle;
    }

    /// <inheritdoc />
    public async Task Cancel(OperationId id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (Registry.TryGet(id, out var handle))
        {
            await handle.RequestCancel().ConfigureAwait(false);
        }

        var neuron = NeuronOperationsQuery.ResolveNeuron(services, brain, id);
        if (!await neuron.RequestCancel().ConfigureAwait(false) && !Registry.TryGet(id, out _))
        {
            var state = await neuron.Get().ConfigureAwait(false);
            if (state is null)
            {
                throw new KeyNotFoundException($"No operation {id} in brain {brain}.");
            }
        }
    }

    /// <inheritdoc />
    public Task<OperationView?> Read(OperationId id, CancellationToken ct) =>
        new NeuronOperationsQuery(services, brain).Read(id, ct);

    /// <inheritdoc />
    public Task Reconcile(OperationId id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return NeuronOperationsQuery.ResolveNeuron(services, brain, id).Reconcile();
    }

    private static string? CaptureDriver(IServiceProvider services)
    {
        var siloDetailsType = Type.GetType("Orleans.Runtime.ILocalSiloDetails, Orleans.Core.Abstractions")
            ?? Type.GetType("Orleans.Runtime.ILocalSiloDetails, Orleans.Core");
        if (siloDetailsType is null)
        {
            return null;
        }

        var details = services.GetService(siloDetailsType);
        if (details is null)
        {
            return null;
        }

        var address = siloDetailsType.GetProperty("SiloAddress")?.GetValue(details);
        return address?.GetType().GetMethod("ToParsableString", Type.EmptyTypes)?.Invoke(address, null) as string;
    }

    private static string? CaptureTraceId(IServiceProvider services)
    {
        var requestContextType = Type.GetType("Orleans.Runtime.RequestContext, Orleans.Core.Abstractions")
            ?? Type.GetType("Orleans.Runtime.RequestContext, Orleans.Core");
        if (requestContextType is not null)
        {
            var getMethod = requestContextType.GetMethod("Get", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static, [typeof(string)]);
            var traceObj = getMethod?.Invoke(null, ["intochatos.trace"]);
            if (traceObj is not null)
            {
                var traceIdProp = traceObj.GetType().GetProperty("TraceId");
                if (traceIdProp?.GetValue(traceObj) is string id && !string.IsNullOrEmpty(id))
                {
                    return id;
                }
            }
        }

        var computeTraceContextType = Type.GetType("DigitalBrain.Capabilities.Compute.TraceContext, DigitalBrain.Capabilities.Compute");
        if (computeTraceContextType is not null)
        {
            var currentProp = computeTraceContextType.GetProperty("Current", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            var currentTrace = currentProp?.GetValue(null);
            if (currentTrace is not null)
            {
                var traceIdProp = currentTrace.GetType().GetProperty("TraceId");
                if (traceIdProp?.GetValue(currentTrace) is string id && !string.IsNullOrEmpty(id))
                {
                    return id;
                }
            }
        }

        return System.Diagnostics.Activity.Current?.TraceId.ToHexString();
    }
}
