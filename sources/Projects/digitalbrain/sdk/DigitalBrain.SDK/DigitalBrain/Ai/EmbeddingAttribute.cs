using DigitalBrain.Runtime.Ai;

namespace DigitalBrain.SDK.DigitalBrain.Ai;

[AttributeUsage(AttributeTargets.Parameter)]
public abstract class EmbeddingAttributeBase : Attribute, IFacetMetadata
{
    public abstract string ServiceKey { get; }
}

[AttributeUsage(AttributeTargets.Parameter)]
public sealed class EmbeddingAttribute<TModel> : EmbeddingAttributeBase
    where TModel : EmbeddingModel, new()
{
    public override string ServiceKey { get; } = new TModel().ServiceKey;
}
