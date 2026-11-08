using DigitalBrain.Operations;
using DigitalBrain.Spi;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Runtime.Orleans.Operations;

/// <summary>
/// <see cref="INeuronContext.OperationsQuery"/> on the Orleans host: the running operations of one brain on this silo
/// (<see cref="OperationRegistry"/>) read with their saved state from the operation entity, and any operation of the brain by id.
/// </summary>
internal sealed class RuntimeOperationsQuery(IServiceProvider services, BrainId brain) : IOperationsQuery
{
    public async Task<IReadOnlyList<OperationView>> Running(CancellationToken ct)
    {
        var registry = services.GetService<OperationRegistry>()
            ?? throw new InvalidOperationException("Operations need the DigitalBrain runtime services (AddDigitalBrainRuntime).");
        var views = new List<OperationView>();
        foreach (var id in registry.Running(brain))
        {
            ct.ThrowIfCancellationRequested();
            var state = await RuntimeOperations.Entity(services, brain, id).Get().ConfigureAwait(false);
            if (state is { Phase: OperationPhase.Running }) { views.Add(View(state)); }
        }
        return views;
    }

    public async Task<OperationView?> Read(OperationId id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var state = await RuntimeOperations.Entity(services, brain, id).Get().ConfigureAwait(false);
        return state is null ? null : View(state);
    }

    private static OperationView View(OperationState state)
        => new(new OperationId(state.Id), state.Kind, state.Origin, state.Fraction, state.Result, state.Started, state.TraceId);
}
