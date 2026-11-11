using DigitalBrain.Confirmation;
using DigitalBrain.Contracts;
using DigitalBrain.Neurons;

namespace DigitalBrain.Operations;

/// <summary>
/// Durable operation neuron hosted identically across runtimes.
/// Keyed by operation id under brain/runtime.operation/id.
/// </summary>
[Neuron("runtime.operation")]
public sealed class OperationNeuron : Neuron<OperationState>, IOperationNeuron, IDisposable
{
    public const string NeuronType = "runtime.operation";

    private CancellationTokenSource? _runningCts;

    public void Dispose()
    {
        _runningCts?.Dispose();
    }

    protected override async Task OnActivate(CancellationToken cancellationToken)
    {
        await Reconcile().ConfigureAwait(true);
    }

    public async Task Reconcile()
    {
        if (State.Phase != OperationPhase.Done)
        {
            var driverLifetime = Seat.Services.GetService(typeof(IOperationDriverLifetime)) as IOperationDriverLifetime;
            bool? ended = null;
            if (State.DriverLifetime is { } token)
            {
                ended = driverLifetime?.HasEnded(token);
            }
            if (ended is null && State.Driver is { } driver)
            {
                ended = CheckClusterMembershipFallback(Seat.Services, driver);
            }
            if (ended == true)
            {
                await Complete(new OperationResult.Failed("interrupted", "The process running this operation stopped. Inspect the destination before retrying.")).ConfigureAwait(true);
                return;
            }
        }

        if (State.Phase == OperationPhase.AwaitingApproval && State.ApprovalPlanId is { } planId)
        {
            var outcome = await Context.Get<IConfirmationRequests>(IConfirmationRequests.Of(Id.Brain)).Outcome(planId).ConfigureAwait(true);
            if (outcome is not null)
            {
                await Settle(outcome).ConfigureAwait(true);
                return;
            }

            await Context.Synapses.Ensure<ConfirmationResolved>(IConfirmationRequests.Of(Id.Brain)).ConfigureAwait(true);
            return;
        }
    }

    public async Task<OperationState> Begin(OperationState initial)
    {
        ArgumentNullException.ThrowIfNull(initial);
        if (State.Exists)
        {
            return State;
        }

        if (initial.Id != Id.Key)
        {
            throw new ArgumentException($"Operation {initial.Id} does not belong to entity {Id}.", nameof(initial));
        }

        var signals = new Signal[] { new OperationStarted(new OperationId(initial.Id), initial.Kind, initial.Origin) };
        await Save(initial, signals).ConfigureAwait(true);
        return State;
    }

    public async Task Prepared(OperationApproval? approval)
    {
        if (State.Phase != OperationPhase.Running)
        {
            return;
        }

        if (approval is null)
        {
            Drive();
            return;
        }

        var transition = State.AwaitApproval(approval.Question.Id, approval.Guard, Context.Time.GetUtcNow());
        if (transition is { } t)
        {
            await Save(t.Next, t.Signals).ConfigureAwait(true);
            await Context.Synapses.Ensure<ConfirmationResolved>(IConfirmationRequests.Of(Id.Brain)).ConfigureAwait(true);
            await Context.Get<IConfirmationRequests>(IConfirmationRequests.Of(Id.Brain)).Request(approval.Question).ConfigureAwait(true);
        }
    }

    [On]
    public async Task OnResolved(ConfirmationResolved resolved)
    {
        ArgumentNullException.ThrowIfNull(resolved);
        if (State.Phase != OperationPhase.AwaitingApproval || !string.Equals(resolved.PlanId, State.ApprovalPlanId, StringComparison.Ordinal))
        {
            return;
        }

        var outcome = await Context.Get<IConfirmationRequests>(IConfirmationRequests.Of(Id.Brain)).Outcome(resolved.PlanId).ConfigureAwait(true);
        if (outcome is not null)
        {
            await Settle(outcome).ConfigureAwait(true);
        }
    }

    private async Task Settle(ConfirmationResult outcome)
    {
        var transition = State.Answer(outcome, Context.Time.GetUtcNow());
        if (transition is { } t)
        {
            await Save(t.Next, t.Signals).ConfigureAwait(true);
        }

        try
        {
            await Context.Synapses.Remove(IConfirmationRequests.Of(Id.Brain)).ConfigureAwait(true);
        }
        catch
        {
            // Best effort unsubscribe
        }

        if (State.Phase == OperationPhase.Running)
        {
            Drive();
        }
        else if (State.Phase == OperationPhase.Done && State.Result is { } result)
        {
            OperationPlanRegistry.Remove(State.Id);
            if ((Seat.Services.GetService(typeof(OperationRegistry)) as OperationRegistry)?.TryGet(new OperationId(State.Id), out var handle) == true)
            {
                handle.NotifyDone(result);
            }
        }
    }

    public async Task<bool> Progress(double fraction)
    {
        var transition = State.Progress(fraction, Context.Time.GetUtcNow());
        if (transition is not { } t)
        {
            return false;
        }

        await Save(t.Next, t.Signals).ConfigureAwait(true);
        return true;
    }

    public async Task<OperationState> Complete(OperationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (State.Phase == OperationPhase.Done)
        {
            return State;
        }

        var transition = State.Complete(result, Context.Time.GetUtcNow());
        if (transition is { } t)
        {
            await Save(t.Next, t.Signals).ConfigureAwait(true);
        }

        OperationPlanRegistry.Remove(State.Id);
        _runningCts?.Cancel();
        if ((Seat.Services.GetService(typeof(OperationRegistry)) as OperationRegistry)?.TryGet(new OperationId(State.Id), out var handle) == true)
        {
            handle.NotifyDone(result);
        }
        return State;
    }

    public async Task<bool> RequestCancel()
    {
        if (State.Phase == OperationPhase.Done)
        {
            return false;
        }

        _runningCts?.Cancel();
        var handlerRuns = _runningCts is not null;
        var transition = State.RequestCancel(handlerRuns, Context.Time.GetUtcNow());
        if (transition is not { } t)
        {
            return false;
        }

        if (t.Signals.Count > 0)
        {
            await Save(t.Next, t.Signals).ConfigureAwait(true);
        }
        else
        {
            await Store(t.Next).ConfigureAwait(true);
        }

        return true;
    }

    public Task<OperationState?> Get() => Task.FromResult<OperationState?>(State.Exists ? State : null);

    private void Drive()
    {
        var handlers = Seat.Services.GetService(typeof(OperationHandlers)) as OperationHandlers;
        if (handlers?.Find(State.Kind) is not { } execution)
        {
            return;
        }

        var runningCts = _runningCts = new CancellationTokenSource();
        var scope = new OperationScope(
            Id.Brain,
            State.Origin,
            Seat.Services,
            State.Guard,
            Seat.Services.GetService(typeof(IOperationsQuery)) as IOperationsQuery ?? new NeuronOperationsQuery(Seat.Services, Id.Brain));

        var plan = OperationPlanRegistry.Take(State.Id) ?? execution.Deserialize(State.PlanJson);
        var self = Context.Get<IOperationNeuron>(Id);

        Context.Detach(async ct =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(runningCts.Token, ct);
            try
            {
                var result = await execution.Run(plan, self, scope, linked.Token).ConfigureAwait(false);
                await self.Complete(result).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested)
            {
                await self.Complete(new OperationResult.Cancelled()).ConfigureAwait(false);
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                await self.Complete(new OperationResult.Failed("error", error.Message)).ConfigureAwait(false);
            }
        });
    }

    private static bool? CheckClusterMembershipFallback(IServiceProvider services, string driver)
    {
        var membershipType = Type.GetType("Orleans.Runtime.IClusterMembershipService, Orleans.Core.Abstractions")
            ?? Type.GetType("Orleans.Runtime.IClusterMembershipService, Orleans.Core")
            ?? AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("Orleans.Runtime.IClusterMembershipService"))
                .FirstOrDefault(t => t is not null);
        if (membershipType is null)
        {
            return null;
        }

        var membership = services.GetService(membershipType);
        if (membership is null)
        {
            return null;
        }

        var snapshotProp = membershipType.GetProperty("CurrentSnapshot");
        var snapshot = snapshotProp?.GetValue(membership);
        if (snapshot is null)
        {
            return null;
        }

        var siloDriverLifetimeType = Type.GetType("DigitalBrain.Runtime.Orleans.Hosting.SiloDriverLifetime, DigitalBrain.Runtime.Orleans")
            ?? AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("DigitalBrain.Runtime.Orleans.Hosting.SiloDriverLifetime"))
                .FirstOrDefault(t => t is not null);
        var method = siloDriverLifetimeType?.GetMethod("DriverHasEnded", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        if (method is not null)
        {
            return method.Invoke(null, [driver, snapshot]) as bool?;
        }

        return null;
    }
}
