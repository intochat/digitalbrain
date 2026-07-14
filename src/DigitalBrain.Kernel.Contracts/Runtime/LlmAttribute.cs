namespace DigitalBrain.Kernel.Llm;

using System.Reflection;
using DigitalBrain.Kernel.Contracts.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Runtime;
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class LlmAttribute<TModel> : Attribute, IFacetMetadata
{
}
public sealed class LlmAttributeMapper<TModel> : IAttributeToFactoryMapper<LlmAttribute<TModel>>
{
    public Factory<IGrainContext, object> GetFactory(ParameterInfo parameter, LlmAttribute<TModel> metadata)
    {
        if (parameter.ParameterType != typeof(IChatClient))
        {
            throw new ArgumentException(
                $"Parameter '{parameter.Name}' on '{parameter.Member.DeclaringType}' must be of type IChatClient "
                + $"because it has an [Llm<{typeof(TModel).Name}>] attribute.",
                parameter.Name);
        }
        var serviceKey = LlmServiceKeys.For(typeof(TModel));
        return context => context.ActivationServices.GetRequiredKeyedService<IChatClient>(serviceKey);
    }
}
public static class LlmServiceKeys
{
    public static string For(Type modelType)
    {
        if (!typeof(DigitalBrainModel).IsAssignableFrom(modelType))
        {
            throw new InvalidOperationException($"Type '{modelType.Name}' used with [Llm<{modelType.Name}>] must derive from DigitalBrainModel.");
        }
        var model = (DigitalBrainModel)Activator.CreateInstance(modelType)!;
        return model.Describe().ServiceKey;
    }
}
