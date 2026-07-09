namespace DigitalBrain.Kernel.Llm;

using System.Reflection;
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
// Model marker types must expose a public static string member (property or const/field) named "ServiceKey".
internal static class LlmServiceKeys
{
    public static string For(Type modelType)
    {
        // FlattenHierarchy: GetProperty/GetField only search static members declared on the exact type
        // unless told otherwise, so a shared ServiceKey on a common base (e.g. LlmModel) would otherwise
        // be invisible to subclasses.
        const BindingFlags staticMemberLookup = BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy;
        var member = modelType.GetProperty("ServiceKey", staticMemberLookup)
            ?? (MemberInfo?)modelType.GetField("ServiceKey", staticMemberLookup);
        if (member is null)
        {
            throw new InvalidOperationException(
                $"Type '{modelType.Name}' used with [Llm<{modelType.Name}>] has no public static ServiceKey member.");
        }

        var value = member switch
        {
            PropertyInfo property => property.GetValue(null) as string,
            FieldInfo field => field.GetValue(null) as string,
            _ => null
        };

        return value ?? throw new InvalidOperationException($"Type '{modelType.Name}'.ServiceKey returned null.");
    }
}
