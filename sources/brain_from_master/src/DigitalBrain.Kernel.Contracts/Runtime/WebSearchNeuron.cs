using DigitalBrain.Kernel.Contracts;
using Orleans;

namespace DigitalBrain.Kernel.Runtime;

[GenerateSerializer, Alias("digitalbrain.web-search.query.v1")]
public sealed record WebSearchQuery(
    [property: Id(0)] string Query,
    [property: Id(1)] int MaximumResults);

[GenerateSerializer, Alias("digitalbrain.web-search.evidence.v1")]
public sealed record WebSearchEvidence(
    [property: Id(0)] string Title,
    [property: Id(1)] string Url,
    [property: Id(2)] string Snippet);

[GenerateSerializer, Alias("digitalbrain.web-search.snapshot.v1")]
public sealed record WebSearchSnapshot(
    [property: Id(0)] WebSearchEvidence[] Results);

[Alias("digitalbrain.web-search.v1")]
public interface IWebSearch : IGrainWithStringKey
{
    [Alias("digitalbrain.web-search.search")]
    Task<WebSearchSnapshot> SearchAsync(
        WebSearchQuery query,
        CancellationToken cancellationToken = default);
}
