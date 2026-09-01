using System.Text.Json;
using DigitalBrain.Abstractions.Execution;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Execution;

namespace DigitalBrain.Google;

internal sealed class GmailSearchHandler(IGmail gmail) : ICapabilityHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public CapabilityId Id { get; } = CapabilityId.Parse("gmail.search");

    public async Task<ContextDelta> InvokeAsync(
        ExecutionId executionId,
        OwnerId owner,
        string requestJson,
        CancellationToken cancellationToken)
    {
        _ = executionId;
        var request = ParseRequest(requestJson);
        var json = await gmail.SearchJsonAsync(owner, request.Account, request.Topic, cancellationToken)
            .ConfigureAwait(false);
        return new ContextDelta(
            new ContextPath("gmail.search"),
            SchemaHash: "gmail.search.v1",
            PayloadJson: json,
            BlobRef: null);
    }

    private static GmailSearchRequest ParseRequest(string requestJson)
    {
        if (string.IsNullOrWhiteSpace(requestJson))
        {
            return GmailSearchRequest.Default;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<GmailSearchRequest>(requestJson, JsonOptions);
            if (parsed is null)
            {
                return GmailSearchRequest.Default;
            }

            return new GmailSearchRequest(
                string.IsNullOrWhiteSpace(parsed.Account) ? GmailSearchRequest.Default.Account : parsed.Account,
                string.IsNullOrWhiteSpace(parsed.Topic) ? GmailSearchRequest.Default.Topic : parsed.Topic);
        }
        catch (JsonException)
        {
            return GmailSearchRequest.Default;
        }
    }

    private sealed record GmailSearchRequest(string Account, string Topic)
    {
        public static GmailSearchRequest Default { get; } = new("", "New Customer");
    }
}
