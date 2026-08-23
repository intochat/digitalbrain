using DigitalBrain.Abstractions.Execution;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Execution;

namespace DigitalBrain.Integrations.Salesforce;

public sealed class SalesforceUpsertHandler(ISalesforceTransport transport) : ICapabilityHandler
{
    public CapabilityId Id { get; } = CapabilityId.Parse("salesforce.upsert");

    public async Task<ContextDelta> InvokeAsync(
        ExecutionId executionId,
        OwnerId owner,
        string requestJson,
        CancellationToken cancellationToken)
    {
        _ = executionId;
        _ = owner;
        var json = await transport.UpsertJsonAsync("Lead", requestJson, cancellationToken)
            .ConfigureAwait(false);
        return new ContextDelta(
            new ContextPath("salesforce.upsert"),
            SchemaHash: "salesforce.upsert.v1",
            PayloadJson: json,
            BlobRef: null);
    }
}
