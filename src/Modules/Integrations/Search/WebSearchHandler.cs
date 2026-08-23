using DigitalBrain.Abstractions.Execution;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Execution;

namespace DigitalBrain.Integrations.Search;

public sealed class WebSearchHandler(IWebSearchTransport transport) : ICapabilityHandler
{
    public CapabilityId Id { get; } = CapabilityId.Parse("websearch.company");

    public async Task<ContextDelta> InvokeAsync(
        ExecutionId executionId,
        OwnerId owner,
        string requestJson,
        CancellationToken cancellationToken)
    {
        _ = executionId;
        _ = owner;
        _ = requestJson;
        var json = await transport.SearchCompanyJsonAsync("Acme", cancellationToken)
            .ConfigureAwait(false);
        return new ContextDelta(
            new ContextPath("websearch.company"),
            SchemaHash: "websearch.company.v1",
            PayloadJson: json,
            BlobRef: null);
    }
}
