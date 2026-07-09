namespace DigitalBrain.Kernel.Voice;

using System.Reflection;
using DigitalBrain.Kernel.Llm;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Runtime;

// Mirrors LlmAttribute<TModel>/LlmAttributeMapper<TModel> (DigitalBrain.Kernel.Abstractions/LlmAttribute.cs)
// for voice-to-text: grain constructors declare [Voice2Text<SomeVoiceModel>] IVoiceTranscriber transcriber
// to get the keyed transcriber registered for that exact model. Lives in DigitalBrain.Kernel itself, not
// Kernel.Abstractions, because IVoiceTranscriber is a project-local interface here (VoiceTranscription.cs)
// and Kernel.Abstractions must not reference Kernel back.
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class Voice2TextAttribute<TModel> : Attribute, IFacetMetadata
{
}

public sealed class Voice2TextAttributeMapper<TModel> : IAttributeToFactoryMapper<Voice2TextAttribute<TModel>>
{
    public Factory<IGrainContext, object> GetFactory(ParameterInfo parameter, Voice2TextAttribute<TModel> metadata)
    {
        if (parameter.ParameterType != typeof(IVoiceTranscriber))
        {
            throw new ArgumentException(
                $"Parameter '{parameter.Name}' on '{parameter.Member.DeclaringType}' must be of type IVoiceTranscriber "
                + $"because it has a [Voice2Text<{typeof(TModel).Name}>] attribute.",
                parameter.Name);
        }

        var serviceKey = LlmServiceKeys.For(typeof(TModel));
        return context => context.ActivationServices.GetRequiredKeyedService<IVoiceTranscriber>(serviceKey);
    }
}
