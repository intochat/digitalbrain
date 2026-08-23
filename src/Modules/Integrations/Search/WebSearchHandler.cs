using System.Text.Json;
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
        var company = ParseCompany(requestJson);
        var json = await transport.SearchCompanyJsonAsync(company, cancellationToken)
            .ConfigureAwait(false);
        return new ContextDelta(
            new ContextPath("websearch.company"),
            SchemaHash: "websearch.company.v1",
            PayloadJson: json,
            BlobRef: null);
    }

    private static string ParseCompany(string requestJson)
    {
        if (string.IsNullOrWhiteSpace(requestJson))
        {
            return "Acme";
        }

        try
        {
            using var document = JsonDocument.Parse(requestJson);
            if (document.RootElement.TryGetProperty("company", out var company)
                && company.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(company.GetString()))
            {
                return company.GetString()!;
            }
        }
        catch (JsonException)
        {
        }

        return "Acme";
    }
}
