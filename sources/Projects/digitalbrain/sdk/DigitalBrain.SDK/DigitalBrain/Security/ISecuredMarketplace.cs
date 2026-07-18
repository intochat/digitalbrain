using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.DigitalBrain.Security;

/// <summary>
/// A strongly-typed C# interface representing the Secured Marketplace.
/// </summary>
public interface ISecuredMarketplace : IGrainWithGuidKey
{
}

[GenerateSerializer]
public sealed record SubmitMarketplaceQuery(
    [property: Id(0)] string BrainId,
    [property: Id(1)] string Query
) : Synapse;

[GenerateSerializer]
public sealed record MarketplaceQueryResponse(
    [property: Id(0)] bool Success,
    [property: Id(1)] string Data,
    [property: Id(2)] string? ErrorMessage = null
) : Synapse;
