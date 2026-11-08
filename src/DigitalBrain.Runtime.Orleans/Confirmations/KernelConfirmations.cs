using System.Collections.Concurrent;
using DigitalBrain.Confirmation;
using DigitalBrain.Runtime.Orleans.Hosting;
using DigitalBrain.Runtime.Orleans.Operations;
using DigitalBrain.Spi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DigitalBrain.Runtime.Orleans.Confirmations;

/// <summary>
/// Implements <see cref="IConfirmationContexts"/> using the <see cref="IConfirmationEntity"/> neuron.
/// </summary>
public sealed class KernelConfirmations(IServiceProvider services, IOptions<ConfirmationOptions> options) : IConfirmationContexts
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<ConfirmationResult>> _pending = new(StringComparer.Ordinal);
    private readonly object _listenerLock = new();
    private Task? _listenerTask;
    private IClusterListeners? _clusterListeners;

    public IConfirmations For(BrainId brain, string requester)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requester);
        return new Bound(this, services, options, brain, requester);
    }

    internal async Task EnsureListenerAsync()
    {
        if (_listenerTask is { IsCompletedSuccessfully: true }) { return; }
        Task task;
        lock (_listenerLock)
        {
            if (_listenerTask is { IsFaulted: true } or { IsCanceled: true })
            {
                _listenerTask = null;
            }

            task = _listenerTask ??= InitializeListenerAsync();
        }

        try
        {
            await task.ConfigureAwait(false);
        }
        catch
        {
            lock (_listenerLock)
            {
                if (ReferenceEquals(_listenerTask, task))
                {
                    _listenerTask = null;
                }
            }
            throw;
        }
    }

    private async Task InitializeListenerAsync()
    {
        _clusterListeners = services.GetService<IClusterListeners>();
        if (_clusterListeners is not null)
        {
            await _clusterListeners.Listen<ConfirmationResolved>("kernel-confirmations", (_, resolved, _) =>
            {
                CompletePending(resolved.PlanId, new ConfirmationResult(resolved.PlanId, resolved.Outcome, resolved.Reason, resolved.Skipped));
                return Task.CompletedTask;
            }).ConfigureAwait(false);
        }
    }

    private void CompletePending(string planId, ConfirmationResult result)
    {
        if (_pending.TryRemove(planId, out var tcs))
        {
            tcs.TrySetResult(result);
        }
    }

    private sealed class Bound(KernelConfirmations owner, IServiceProvider services, IOptions<ConfirmationOptions> options, BrainId brain, string requester) : IConfirmations
    {
        public ConfirmationPolicy Policy => options.Value.For(brain.Value);

        private IConfirmationEntity Entity => SiloNeurons.Get<IConfirmationEntity>(services, new DigitalBrain.NeuronId(brain, "runtime.confirmations", "confirmations"));

        public async Task<ConfirmationResult> Confirm(ConfirmationPlan plan, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            await owner.EnsureListenerAsync().ConfigureAwait(false);
            var entity = Entity;
            var created = new TaskCompletionSource<ConfirmationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            // A plan raised again while this host still waits on it (a flow recovering a lost ask) shares the one wait.
            var tcs = owner._pending.GetOrAdd(plan.Id, created);
            try
            {
                await entity.Begin(plan, requester).WaitAsync(ct).ConfigureAwait(false);
                if (await entity.Result(plan.Id).WaitAsync(ct).ConfigureAwait(false) is { } immediate)
                {
                    owner._pending.TryRemove(KeyValuePair.Create(plan.Id, tcs));
                    return immediate;
                }

                if (owner._clusterListeners is null)
                {
                    while (!tcs.Task.IsCompleted)
                    {
                        if (await entity.Result(plan.Id).WaitAsync(ct).ConfigureAwait(false) is { } polled)
                        {
                            owner.CompletePending(plan.Id, polled);
                            return polled;
                        }
                        await Task.Delay(50, ct).ConfigureAwait(false);
                    }
                }

                return await tcs.Task.WaitAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                owner._pending.TryRemove(KeyValuePair.Create(plan.Id, tcs));
                await entity.Cancel(plan.Id).ConfigureAwait(false);
                throw;
            }
            finally
            {
                owner._pending.TryRemove(KeyValuePair.Create(plan.Id, tcs));
            }
        }

        public async Task Resolve(string planId, bool approve, CancellationToken ct = default)
        {
            await Entity.ResolveAsRequester(planId, approve).WaitAsync(ct).ConfigureAwait(false);
            if (await Entity.Result(planId).WaitAsync(ct).ConfigureAwait(false) is { } answer)
            {
                owner.CompletePending(planId, answer);
            }
        }
    }
}
