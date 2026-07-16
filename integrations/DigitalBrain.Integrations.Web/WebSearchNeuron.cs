using DigitalBrain.Integrations.Web.Contracts;
using DigitalBrain.Kernel.Runtime;
using Microsoft.Extensions.Logging;
using Orleans;

namespace DigitalBrain.Integrations.Web;

[GrainType("digitalbrain.web-search.v1")]
internal sealed class WebSearchNeuron(
    BraveWebSearchClient client,
    ILogger<WebSearchNeuron> logger) : Grain, IWebSearch
{
    public async Task<WebSearchSnapshot> SearchAsync(
        WebSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await client.SearchAsync(
                new WebSearchRequest(query.Query, query.MaximumResults),
                cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return new WebSearchSnapshot(response.Results
                .Select(static result => new WebSearchEvidence(
                    result.Title,
                    result.Url,
                    result.Snippet))
                .ToArray());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                "Web search failed with {ExceptionType}.",
                exception.GetType().Name);
            throw new InvalidOperationException("Web search is unavailable.");
        }
    }
}
