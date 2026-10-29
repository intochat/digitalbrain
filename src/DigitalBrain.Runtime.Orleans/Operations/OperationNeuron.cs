using System.Text.Json;
using DigitalBrain.Capabilities.Storage;
using DigitalBrain.Contracts;
using DigitalBrain.Invocation;
using DigitalBrain.Operations;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;

namespace DigitalBrain.Runtime.Orleans.Operations;

/// <summary>The runtime-internal contract of the operation entity; <see cref="RuntimeOperations"/> is its only caller.</summary>
[NeuronAlias("runtime.operation")]
internal interface IOperationEntity
{
    /// <summary>Creates the entity: saves <paramref name="initial"/> and journals <see cref="OperationStarted"/> in one entry. A second call returns the saved state.</summary>
    Task<OperationState> Begin(OperationState initial);

    /// <summary>Saves progress with <see cref="OperationProgress"/>; false (nothing saved) once cancel was requested or the operation is done.</summary>
    Task<bool> Progress(double fraction);

    /// <summary>Saves the result with <see cref="OperationDone"/>; once only, later calls are ignored.</summary>
    Task<OperationState> Complete(OperationResult result);

    /// <summary>Records a cancel request; false when the operation is already done or unknown.</summary>
    Task<bool> RequestCancel();

    /// <summary>Ends a running operation only when its recorded driver is known to have died.</summary>
    Task Reconcile();

    /// <summary>The saved state, or null when no operation has this id.</summary>
    [ReadOnlyCall]
    Task<OperationState?> Get();
}

/// <summary>
/// The operation entity (D-04): one per operation, keyed by its <see cref="OperationId"/>, state over the brain's journal. Every
/// change is saved together with its signal (<see cref="OperationStarted"/>, <see cref="OperationProgress"/>,
/// <see cref="OperationDone"/>) as one entry, so a signal is published only after its state is durable. The entity holds state
/// only: the work runs in <see cref="RuntimeOperations"/>, so reactivating a done operation never runs it again.
/// </summary>
[Neuron("runtime.operation")]
internal sealed class OperationNeuron : Neuron, IOperationEntity
{
    internal const string NeuronType = "runtime.operation";

    private OperationState? _state;

    protected override async Task OnActivate(CancellationToken cancellationToken)
    {
        _state = await Context.State.Load<OperationState>().ConfigureAwait(true);
        await Reconcile().ConfigureAwait(true);
    }

    public async Task Reconcile()
    {
        if (_state is not { Phase: OperationPhase.Running } state) { return; }
        var ended = state.DriverLifetime is { } token
            ? Context.Services.GetService<IOperationDriverLifetime>()?.HasEnded(token)
            : null;
        // A live process can still own background work after its silo leaves membership. Unknown process evidence
        // leaves the original membership proof unchanged, including for older operations without a lifetime token.
        if (ended == false) { return; }
        if (ended is null && (state.Driver is not { } driver
            || Context.Services.GetService<IClusterMembershipService>() is not { } membership
            || !DriverHasEnded(driver, membership.CurrentSnapshot))) { return; }
        await Complete(new OperationResult.Failed("interrupted", "The process running this operation stopped. Inspect the destination before retrying.")).ConfigureAwait(true);
    }

    internal static bool DriverHasEnded(string driver, ClusterMembershipSnapshot snapshot)
    {
        if (!SiloAddress.TryParse(driver, out var address)) { return false; }
        if (snapshot.Members.TryGetValue(address, out var member)) { return member.Status == SiloStatus.Dead; }
        // A new incarnation bound to the same endpoint proves the old process has gone. Missing membership alone
        // proves nothing: a live remote driver can be temporarily absent from this silo's view of the cluster.
        return snapshot.Members.Values.Any(current => current.Status == SiloStatus.Active && current.SiloAddress.IsSuccessorOf(address));
    }

    public async Task<OperationState> Begin(OperationState initial)
    {
        ArgumentNullException.ThrowIfNull(initial);
        if (_state is not null) { return _state; }
        if (initial.Id != Id.Key) { throw new ArgumentException($"Operation {initial.Id} does not belong to entity {Id}.", nameof(initial)); }
        IReadOnlyList<Signal> signals = initial.Kind == nameof(WritePlan)
            ? [new OperationStarted(new OperationId(initial.Id), JsonSerializer.Deserialize<WritePlan>(initial.PlanJson, RuntimeProtocol.Json)!)]
            : [];
        await Context.State.Save(initial, signals).ConfigureAwait(true);
        return _state = initial;
    }

    public async Task<bool> Progress(double fraction)
    {
        if (_state is not { Phase: OperationPhase.Running, CancelRequested: false } state) { return false; }
        var clamped = Math.Clamp(fraction, 0d, 1d);
        var next = state with { Fraction = clamped, Updated = Context.Time.GetUtcNow() };
        await Context.State.Save(next, [new OperationProgress(new OperationId(state.Id), clamped)]).ConfigureAwait(true);
        _state = next;
        return true;
    }

    public async Task<OperationState> Complete(OperationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var state = _state ?? throw new InvalidOperationException($"Operation {Id.Key} was never started.");
        if (state.Phase == OperationPhase.Done) { return state; }
        var next = state with
        {
            Phase = OperationPhase.Done,
            Result = result,
            Fraction = result is OperationResult.Succeeded ? 1 : state.Fraction,
            Updated = Context.Time.GetUtcNow(),
        };
        await Context.State.Save(next, [new OperationDone(new OperationId(state.Id), result)]).ConfigureAwait(true);
        return _state = next;
    }

    public async Task<bool> RequestCancel()
    {
        if (_state is not { Phase: OperationPhase.Running } state) { return false; }
        if (state.CancelRequested) { return true; }
        var next = state with { CancelRequested = true, Updated = Context.Time.GetUtcNow() };
        await Context.State.Save(next, []).ConfigureAwait(true);
        _state = next;
        return true;
    }

    public Task<OperationState?> Get() => Task.FromResult(_state);
}
