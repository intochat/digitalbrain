using DigitalBrain.Product.Enrichment;
using DigitalBrain.Product.SalesInsights;

namespace DigitalBrain.Product.Conversation;

/// <summary>
/// Converts a chat ingress message into a targeted enrichment run without treating chat context as a hosting scope.
/// </summary>
public sealed class ConversationIngressNeuron : Neuron, INeuron<ChatEnrichmentRequested>
    , INeuron<ChatSalesRequested>
{
    public const string Kind = "conversation-ingress";

    public Task HandleAsync(ChatEnrichmentRequested synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        if (!Origin.IsExternalIngress
            || !string.Equals(Id.Name, Origin.Source.Name, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        Emit(
            new AccountEnrichmentStarted(synapse.Request),
            Dispatch.Direct(new NeuronId(AccountEnrichmentNeuron.Kind, synapse.Request.RunId)));
        return Task.CompletedTask;
    }

    public Task HandleAsync(ChatSalesRequested synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        if (!Origin.IsExternalIngress
            || !string.Equals(Id.Name, Origin.Source.Name, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        Emit(
            new SalesInsightRequested(
                synapse.Query,
                new SalesInsightContext(SalesInsightContextKind.ChatConversation, Origin.Source.Name)),
            Dispatch.Direct(new NeuronId(SalesInsightNeuron.Kind, synapse.Query.QueryId)));
        return Task.CompletedTask;
    }
}
