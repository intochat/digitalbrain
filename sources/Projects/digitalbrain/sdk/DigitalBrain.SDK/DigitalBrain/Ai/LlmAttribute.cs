using DigitalBrain.SDK.DigitalBrain.Ai.Models;

namespace DigitalBrain.SDK.DigitalBrain.Ai;

[AttributeUsage(AttributeTargets.Parameter)]
public abstract class LlmAttributeBase : Attribute, IFacetMetadata
{
    public abstract string ServiceKey { get; }
}

[AttributeUsage(AttributeTargets.Parameter)]
public sealed class LlmAttribute<TModel> : LlmAttributeBase
    where TModel : LlmModel, new()
{
    public override string ServiceKey { get; } = new TModel().ServiceKey;
}
