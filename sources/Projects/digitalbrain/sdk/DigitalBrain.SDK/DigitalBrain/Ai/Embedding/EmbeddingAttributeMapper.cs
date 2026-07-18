using System.Reflection;
using DigitalBrain.Runtime.Ai;
using DigitalBrain.SDK.DigitalBrain.Ai;
using Microsoft.Extensions.AI;

namespace DigitalBrain.SDK.DigitalBrain.Ai.Embedding;

public sealed class EmbeddingAttributeMapper<TModel>
    : IAttributeToFactoryMapper<EmbeddingAttribute<TModel>>
    where TModel : EmbeddingModel, new()
{
    public Factory<IGrainContext, object> GetFactory(
        ParameterInfo parameter, EmbeddingAttribute<TModel> metadata)
    {
        if (parameter.ParameterType != typeof(IEmbeddingGenerator<string, Embedding<float>>))
            throw new InvalidOperationException(
                $"[Embedding<{typeof(TModel).Name}>] must bind an IEmbeddingGenerator<string, Embedding<float>> parameter.");
        return context => context.ActivationServices
            .GetKeyedService<IEmbeddingGenerator<string, Embedding<float>>>(metadata.ServiceKey)
            ?? throw new InvalidOperationException(
                $"Embedding model {typeof(TModel).Name} not configured "
                + $"(service key '{metadata.ServiceKey}').");
    }
}
