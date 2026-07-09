namespace DigitalBrain.Kernel.Llm;

using System.Reflection;
using DigitalBrain.Core.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Runtime;

// Grain constructors declare [Llm<SomeModel>] IChatClient chatClient to get the keyed client Task 4
// registered for that exact model — plugging into Orleans' own constructor-facet extensibility point
// (the same one [PersistentState(...)] uses), not a bespoke DI convention. Verified against Orleans'
// real IFacetMetadata/IAttributeToFactoryMapper/GrainConstructorArgumentFactory source.
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

// Maps a model marker type to the ServiceKey DigitalBrainChatClientRegistration registered it under, so
// [Llm<TModel>] can stay a zero-argument generic instead of repeating the key as a string literal.
// Derives the key the same way every other consumer does — instantiate the model and read its own
// Describe().ServiceKey — rather than reflecting for a separately-declared string member (nothing in
// DigitalBrain.Core.Models declares one; DigitalBrainModelDescriptor.ServiceKey is the single source of truth).
// Public (not internal): Voice2TextAttributeMapper<TModel> in DigitalBrain.Kernel reuses this exact
// helper for [Voice2Text<TModel>], and there is no InternalsVisibleTo between the two assemblies.
public static class LlmServiceKeys
{
    public static string For(Type modelType)
    {
        if (!typeof(DigitalBrainModel).IsAssignableFrom(modelType))
        {
            throw new InvalidOperationException(
                $"Type '{modelType.Name}' used with [Llm<{modelType.Name}>] must derive from DigitalBrainModel.");
        }

        var model = (DigitalBrainModel)Activator.CreateInstance(modelType)!;
        return model.Describe().ServiceKey;
    }
}
