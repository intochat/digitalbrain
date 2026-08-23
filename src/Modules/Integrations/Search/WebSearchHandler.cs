using DigitalBrain.Abstractions.Execution;
using DigitalBrain.Execution;

namespace DigitalBrain.Integrations.Search;

public sealed class WebSearchHandler(IWebSearchTransport transport) : ICapabilityHandler
{
    public CapabilityId Id { get; } = CapabilityId.Parse("websearch.company");

    public async Task<ContextDelta> InvokeAsync(
        ExecutionId executionId,
        string requestJson,
        CancellationToken cancellationToken)
    {
        var json = await transport.SearchCompanyJsonAsync("Acme", cancellationToken)
            .ConfigureAwait(false);
        return new ContextDelta(
            new ContextPath("websearch.company"),
            SchemaHash: "websearch.company.v1",
            PayloadJson: json,
            BlobRef: null);
    }
}
