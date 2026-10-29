using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using DigitalBrain.Capabilities;
using DigitalBrain.Capabilities.Storage;
using DigitalBrain.Invocation;
using DigitalBrain.Operations;
using DigitalBrain.Runtime.Orleans.Capabilities;
using DigitalBrain.Runtime.Orleans.Hosting;
using DigitalBrain.Spi;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;

namespace DigitalBrain.Runtime.Orleans.Operations;

/// <summary>
/// <see cref="INeuronContext.Operations"/> on the Orleans host (D-04). <see cref="Start"/> creates the operation entity
/// <c>brain/runtime.operation/id</c> (its state and <see cref="OperationStarted"/> saved as one entry, so the signal follows the
/// durable state), asks <see cref="IOperationGate"/>, then: a <see cref="WritePlan"/> runs in the background through
/// <see cref="StorageWriteRunner"/> against the brain's own storage connections (resolved by the runtime broker for the entity's
/// brain only); any other plan is driven by its caller through the returned handle. Progress is saved at most every 250 ms (and
/// always at 1); the result is saved once with <see cref="OperationDone"/>.
/// </summary>
internal sealed class RuntimeOperations(IServiceProvider services, BrainId brain, string origin) : IOperations
{
    /// <summary>The shortest gap between two saved progress reports.</summary>
    internal static readonly TimeSpan ProgressEvery = TimeSpan.FromMilliseconds(250);

    private OperationRegistry Registry => services.GetService<OperationRegistry>()
        ?? throw new InvalidOperationException("Operations need the DigitalBrain runtime services (AddDigitalBrainRuntime).");

    public async Task<IOperation> Start(OperationPlan plan, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var registry = Registry;
        var time = services.GetService<TimeProvider>() ?? TimeProvider.System;
        var id = new OperationId("op-" + Guid.NewGuid().ToString("N"));
        var entity = Entity(services, brain, id);
        await entity.Begin(Initial(id, plan, time.GetUtcNow())).ConfigureAwait(false);
        var handle = new RuntimeOperation(id, entity, time, registry, plan is WritePlan);
        registry.Add(handle);

        if (plan is WritePlan write)
        {
            _ = Task.Run(() => CheckAndDrive(write, handle, ct), CancellationToken.None);
            return handle;
        }
        var gate = services.GetService<IOperationGate>() ?? new AllowAllOperations();
        var decision = await gate.Check(new OperationRequest(id, brain, origin, plan), ct).ConfigureAwait(false);
        if (!decision.Allowed)
        {
            await handle.Complete(new OperationResult.Failed("denied", decision.Reason ?? "The operation was refused.")).ConfigureAwait(false);
            await handle.RequestCancel().ConfigureAwait(false);
            return handle;
        }
        return handle;
    }

    // The handle is available before a human answers, so the UI can show progress and cancel the pending operation.
    private async Task CheckAndDrive(WritePlan plan, RuntimeOperation handle, CancellationToken ct)
    {
        using var pending = CancellationTokenSource.CreateLinkedTokenSource(ct, handle.Cancellation);
        try
        {
            var gate = services.GetService<IOperationGate>() ?? new ConfirmingOperationGate(services);
            var decision = await gate.Check(new OperationRequest(handle.Id, brain, origin, plan), pending.Token).ConfigureAwait(false);
            pending.Token.ThrowIfCancellationRequested();
            if (!decision.Allowed)
            {
                await handle.Complete(new OperationResult.Failed("denied", decision.Reason ?? "The operation was refused.")).ConfigureAwait(false);
                return;
            }
            using var approved = services.GetService<IStorageWriteAuthorizer>()?.BeginApproved(brain, origin);
            await Drive(Runner(), plan, handle).ConfigureAwait(false);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            var result = pending.IsCancellationRequested ? (OperationResult)new OperationResult.Cancelled()
                : error is StorageException storage ? new OperationResult.Failed(storage.Code, storage.Message)
                : new OperationResult.Failed("denied", "The operation could not be authorized.");
            try { await handle.Complete(result).ConfigureAwait(false); }
            catch (Exception completionError) when (completionError is not OutOfMemoryException) { handle.Fault(completionError); }
        }
    }

    /// <summary>The write runner over this brain's storage connections.</summary>
    internal StorageWriteRunner Runner() => new(Owner, services.GetService<StorageWriteResult>() ?? WaitForStorage);

    internal Task Reconcile(OperationId id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Entity(services, brain, id).Reconcile();
    }

    public async Task Cancel(OperationId id, CancellationToken ct)
    {
        var entity = Entity(services, brain, id);
        await entity.Reconcile().ConfigureAwait(false);
        var known = await entity.RequestCancel().ConfigureAwait(false);
        if (Registry.TryGet(id, out var handle)) { await handle.RequestCancel().ConfigureAwait(false); }
        else if (!known && await Entity(services, brain, id).Get().ConfigureAwait(false) is null)
        {
            throw new KeyNotFoundException($"No operation {id} in brain {brain}.");
        }
    }

    /// <summary>Completes with the result of operation <paramref name="id"/> of <paramref name="brain"/>: from the local handle, else by reading its entity.</summary>
    internal static async Task<OperationResult> WaitFor(IServiceProvider services, BrainId brain, OperationId id, CancellationToken ct)
    {
        if (services.GetRequiredService<OperationRegistry>().TryGet(id, out var handle)) { return await handle.Completion.WaitAsync(ct).ConfigureAwait(false); }
        var entity = Entity(services, brain, id);
        while (true)
        {
            await entity.Reconcile().ConfigureAwait(false);
            var state = await entity.Get().ConfigureAwait(false) ?? throw new KeyNotFoundException($"No operation {id} in brain {brain}.");
            if (state is { Phase: OperationPhase.Done, Result: { } result }) { return result; }
            await Task.Delay(100, ct).ConfigureAwait(false);
        }
    }

    internal static IOperationEntity Entity(IServiceProvider services, BrainId brain, OperationId id)
        => NeuronHosting.Get<IOperationEntity>(services, new NeuronId(brain, OperationNeuron.NeuronType, id.Value));

    private OperationState Initial(OperationId id, OperationPlan plan, DateTimeOffset now)
    {
        string json;
        long? length = null;
        string? hash = null;
        if (plan is WritePlan write)
        {
            if (write.Inline is { } inline)
            {
                length = inline.Length;
                hash = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(inline.Span));
            }
            json = JsonSerializer.Serialize(write with { Inline = null }, RuntimeProtocol.Json);
        }
        else
        {
            json = JsonSerializer.Serialize(plan, plan.GetType(), RuntimeProtocol.Json);
        }
        return new OperationState(id.Value, brain.Value, origin, plan.GetType().Name, json, length, hash, OperationPhase.Running, false, 0, null, now, now,
            services.GetRequiredService<ILocalSiloDetails>().SiloAddress.ToParsableString(),
            services.GetService<IOperationDriverLifetime>()?.Capture());
    }

    private static async Task Drive(StorageWriteRunner runner, WritePlan plan, RuntimeOperation handle)
    {
        OperationResult result;
        try { result = await runner.Run(plan, handle).ConfigureAwait(false); }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            result = handle.Cancellation.IsCancellationRequested ? new OperationResult.Cancelled() : new OperationResult.Failed("error", error.GetType().Name);
        }
        try { await handle.Complete(result).ConfigureAwait(false); }
        catch (Exception error) when (error is not OutOfMemoryException) { handle.Fault(error); }
    }

    // The broker's connection-qualified owner, or the only connection recognizing a legacy id. Ambiguous ids fail closed.
    private async Task<CapabilityConnection<IStorage>> Owner(ItemId item, CancellationToken ct)
    {
        var broker = services.GetRequiredService<CapabilityBroker>().ForApp(origin);
        var candidates = await broker.ResolveFor(brain, item, ct).ConfigureAwait(false);
        switch (candidates.Count)
        {
            case 0: throw new Unavailable(item);
            case 1: return candidates[0];
        }
        CapabilityConnection<IStorage>? owner = null;
        foreach (var candidate in candidates)
        {
            try
            {
                _ = await candidate.Capability.Stat(item, ct).ConfigureAwait(false);
                if (owner is not null) { throw new Conflict(item); }
                owner = candidate;
            }
            catch (StorageException error) when (error is NotFound or Denied or Unsupported)
            {
                // Not this connection's id.
            }
        }
        return owner ?? throw new NotFound(item);
    }

    private Task<OperationResult> WaitForStorage(IStorage storage, OperationId id, CancellationToken ct) => WaitFor(services, brain, id, ct);
}

/// <summary>The handle of a runtime operation: reports and completion go to its entity; cancellation is local and cross-silo.</summary>
internal sealed class RuntimeOperation(OperationId id, IOperationEntity entity, TimeProvider time, OperationRegistry registry, bool driven) : IOperation, IDisposable
{
    private readonly CancellationTokenSource _cancellation = new();
    private readonly TaskCompletionSource<OperationResult> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _lastReport = DateTimeOffset.MinValue;
    private int _completing;

    public OperationId Id { get; } = id;

    public CancellationToken Cancellation => _cancellation.Token;

    /// <summary>Completes with the result once the operation ended.</summary>
    public Task<OperationResult> Completion => _done.Task;

    /// <summary>Saved progress reports (after throttling), for facts.</summary>
    internal int Reports { get; private set; }

    public async ValueTask Report(double fraction)
    {
        if (_cancellation.IsCancellationRequested || Volatile.Read(ref _completing) != 0) { return; }
        var now = time.GetUtcNow();
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_cancellation.IsCancellationRequested || (fraction < 1 && now - _lastReport < RuntimeOperations.ProgressEvery)) { return; }
            _lastReport = now;
            if (await entity.Progress(fraction).ConfigureAwait(false)) { Reports++; }
            else { await _cancellation.CancelAsync().ConfigureAwait(false); }
        }
        finally { _gate.Release(); }
    }

    public async ValueTask Complete(OperationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (Interlocked.Exchange(ref _completing, 1) != 0) { return; }
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var saved = await entity.Complete(result).ConfigureAwait(false);
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
            registry.Remove(Id);
        }
    }

    /// <summary>Fires the token; a caller-driven operation also ends <see cref="OperationResult.Cancelled"/> now, a driven one when its runner stops.</summary>
    internal async Task RequestCancel()
    {
        await _cancellation.CancelAsync().ConfigureAwait(false);
        if (!driven) { await Complete(new OperationResult.Cancelled()).ConfigureAwait(false); }
    }

    internal void Fault(Exception error)
    {
        _done.TrySetException(error);
        registry.Remove(Id);
    }

    public void Dispose()
    {
        _cancellation.Dispose();
        _gate.Dispose();
    }
}

/// <summary>The operations running on this silo, by id: local cancel and local waits.</summary>
internal sealed class OperationRegistry
{
    private readonly ConcurrentDictionary<OperationId, RuntimeOperation> _running = new();

    public void Add(RuntimeOperation operation) => _running[operation.Id] = operation;

    public bool TryGet(OperationId id, out RuntimeOperation operation) => _running.TryGetValue(id, out operation!);

    public void Remove(OperationId id) => _running.TryRemove(id, out _);
}
