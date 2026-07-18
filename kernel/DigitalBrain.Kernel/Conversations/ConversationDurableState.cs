using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;

namespace DigitalBrain.Kernel;

internal sealed class ConversationDurableState
{
    internal ConversationDurableState(
        [FromKeyedServices(nameof(Intents))]
        IDurableDictionary<Guid, ConversationTurnRequest> intents,
        [FromKeyedServices(nameof(Turns))]
        IDurableDictionary<Guid, ConversationTurn> turns,
        [FromKeyedServices(nameof(Results))]
        IDurableDictionary<Guid, ConversationTurnResult> results,
        [FromKeyedServices(nameof(Revision))]
        IDurableValue<long> revision)
    {
        Intents = intents;
        Turns = turns;
        Results = results;
        Revision = revision;
    }

    internal IDurableDictionary<Guid, ConversationTurnRequest> Intents { get; }
    internal IDurableDictionary<Guid, ConversationTurn> Turns { get; }
    internal IDurableDictionary<Guid, ConversationTurnResult> Results { get; }
    internal IDurableValue<long> Revision { get; }
}
