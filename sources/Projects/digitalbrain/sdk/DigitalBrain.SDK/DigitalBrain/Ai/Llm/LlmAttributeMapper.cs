using System.Reflection;
using DigitalBrain.SDK.DigitalBrain.Ai;
using DigitalBrain.SDK.DigitalBrain.Ai.Models;
using Microsoft.Extensions.AI;

namespace DigitalBrain.SDK.DigitalBrain.Ai.Llm;

public sealed class LlmAttributeMapper<TModel>
    : IAttributeToFactoryMapper<LlmAttribute<TModel>>
    where TModel : LlmModel, new()
{
    public Factory<IGrainContext, object> GetFactory(
        ParameterInfo parameter, LlmAttribute<TModel> metadata)
    {
        if (parameter.ParameterType != typeof(IChatClient))
            throw new InvalidOperationException(
                $"[Llm<{typeof(TModel).Name}>] must bind an IChatClient parameter.");
        return context => context.ActivationServices
            .GetKeyedService<IChatClient>(metadata.ServiceKey)
            ?? throw new InvalidOperationException(
                $"LLM model {typeof(TModel).Name} not configured "
                + $"(service key '{metadata.ServiceKey}').");
    }
}
