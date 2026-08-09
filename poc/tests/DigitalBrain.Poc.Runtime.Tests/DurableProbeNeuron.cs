using DigitalBrain.Poc.Abstractions;
using DigitalBrain.Poc.Runtime;

namespace DigitalBrain.Poc.Runtime.Tests;

internal sealed class DurableProbeNeuron(DurableTurn turns)
{
    public Task HandleAsync(IncrementAndEmit input, CancellationToken cancellationToken) =>
        turns.ExecuteAsync(
            input.ReceiptId,
            input.GetType().Name,
            "probe-count",
            0,
            async (state, brain) =>
            {
                state.Replace(state.Value + 1);
                await brain.FireSynapse(new Emitted(), cancellationToken);
            },
            cancellationToken);

    public Task HandleAsync(ThrowAfterStateAndEmit input, CancellationToken cancellationToken) =>
        turns.ExecuteAsync(
            input.ReceiptId,
            input.GetType().Name,
            "probe-count",
            0,
            async (state, brain) =>
            {
                state.Replace(state.Value + 1);
                await brain.FireSynapse(new Emitted(), cancellationToken);
                throw new ProbeFailureException();
            },
            cancellationToken);

    public Task HandleAsync(ReplaceProbeState input, CancellationToken cancellationToken) =>
        turns.ExecuteAsync(
            input.ReceiptId,
            input.GetType().Name,
            "probe-text",
            string.Empty,
            (state, _) =>
            {
                state.Replace(input.Value);
                return Task.CompletedTask;
            },
            cancellationToken);

    public Task<int> ReadCountAsync(CancellationToken cancellationToken = default) =>
        turns.ReadStateAsync("probe-count", 0, cancellationToken);
}
