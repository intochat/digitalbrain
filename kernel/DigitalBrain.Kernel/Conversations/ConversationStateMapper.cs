using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using System.Reflection;

namespace DigitalBrain.Kernel;

internal sealed class ConversationStateMapper
    : IAttributeToFactoryMapper<ConversationStateAttribute>
{
    public Factory<IGrainContext, object> GetFactory(
        ParameterInfo parameter,
        ConversationStateAttribute metadata)
    {
        if (parameter.ParameterType != typeof(ConversationDurableState))
            throw new InvalidOperationException(
                $"Parameter '{parameter.Name}' must be of type {nameof(ConversationDurableState)}.");

        return context =>
        {
            var services = context.ActivationServices;
            return new ConversationDurableState(
                services.GetRequiredKeyedService<
                    IDurableDictionary<Guid, ConversationTurnRequest>>(
                    nameof(ConversationDurableState.Intents)),
                services.GetRequiredKeyedService<
                    IDurableDictionary<Guid, ConversationTurn>>(
                    nameof(ConversationDurableState.Turns)),
                services.GetRequiredKeyedService<
                    IDurableDictionary<Guid, ConversationTurnResult>>(
                    nameof(ConversationDurableState.Results)),
                services.GetRequiredKeyedService<IDurableValue<long>>(
                    nameof(ConversationDurableState.Revision)));
        };
    }
}
