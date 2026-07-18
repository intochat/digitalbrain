using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting.DigitalBrain;

[AspireExport]
public sealed class AnthropicResource(
    string name,
    Uri endpoint,
    ParameterResource apiKey,
    string modelId,
    DigitalBrainResource parent)
    : Resource(name),
      IResourceWithConnectionString,
      IResourceWithParent<DigitalBrainResource>
{
    public Uri Endpoint { get; } = endpoint;

    public ParameterResource ApiKey { get; } = apiKey;

    public string ModelId { get; } = modelId;

    public DigitalBrainResource Parent { get; } = parent;

    public ReferenceExpression ConnectionStringExpression =>
        ReferenceExpression.Create(
            $"Endpoint={Endpoint.AbsoluteUri};Key={ApiKey};Model={ModelId}");

    IEnumerable<KeyValuePair<string, ReferenceExpression>>
        IResourceWithConnectionString.GetConnectionProperties()
    {
        yield return new(
            "Endpoint",
            ReferenceExpression.Create($"{Endpoint.AbsoluteUri}"));
        yield return new(
            "Uri",
            ReferenceExpression.Create($"{Endpoint.AbsoluteUri}"));
        yield return new(
            "Key",
            ReferenceExpression.Create($"{ApiKey}"));
        yield return new(
            "ModelName",
            ReferenceExpression.Create($"{ModelId}"));
    }
}
