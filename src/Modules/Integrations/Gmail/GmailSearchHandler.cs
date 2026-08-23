using DigitalBrain.Abstractions.Execution;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Execution;

namespace DigitalBrain.Integrations.Gmail;

public sealed class GmailSearchHandler(IGmailTransport transport) : ICapabilityHandler
{
    public CapabilityId Id { get; } = CapabilityId.Parse("gmail.search");

    public async Task<ContextDelta> InvokeAsync(
        ExecutionId executionId,
        OwnerId owner,
        string requestJson,
        CancellationToken cancellationToken)
    {
        _ = executionId;
        _ = owner;
        _ = requestJson;
        var json = await transport.SearchJsonAsync("fake", "New Customer", cancellationToken)
            .ConfigureAwait(false);
        return new ContextDelta(
            new ContextPath("gmail.search"),
            SchemaHash: "gmail.search.v1",
            PayloadJson: json,
            BlobRef: null);
    }
}
