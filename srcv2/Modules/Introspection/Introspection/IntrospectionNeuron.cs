using DigitalBrain.Abstractions;
using DigitalBrain.Core;

namespace DigitalBrain.Introspection;

[GrainType("introspection")]
public sealed class IntrospectionNeuron :
    Neuron,
    IIntrospection,
    IHandle<TallyJournalRequest>,
    IHandle<ReadJournalRequest>,
    IHandle<ReadTopologyRequest>
{
    public async Task HandleAsync(TallyJournalRequest synapse, CancellationToken cancellationToken)
    {
        var response = await new IntrospectionJournalReader(GrainFactory, Id.Owner)
            .TallyAsync(synapse, cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        await ReplyAsync(response, cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    public async Task HandleAsync(ReadJournalRequest synapse, CancellationToken cancellationToken)
    {
        var response = await new IntrospectionJournalReader(GrainFactory, Id.Owner)
            .ReadPageAsync(synapse, cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        await ReplyAsync(response, cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    public async Task HandleAsync(ReadTopologyRequest synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);

        var response = await new IntrospectionTopologyReader(
                GrainFactory,
                ServiceProvider,
                TimeProvider,
                Id.Owner)
            .ReadAsync(synapse.CommandId, cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        await ReplyAsync(response, cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }
}
