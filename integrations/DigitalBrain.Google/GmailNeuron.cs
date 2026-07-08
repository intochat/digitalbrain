using DigitalBrain.Core;
using DigitalBrain.Google;
using DigitalBrain.Kernel;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Google;

[GrainType("digitalbrain.google.gmail.v1")]
public class GmailNeuron(ILogger<GmailNeuron> logger, NeuronJournals journals, IGmailApiClientFactory gmailApiClientFactory)
    : Neuron(logger, journals), IGmailNeuron
{
    public async Task<string[]> ListMessagesAsync(string query, int maxResults = 20, CancellationToken ct = default)
    {
        var client = await gmailApiClientFactory.CreateAsync(Self.AsScope(), ct);
        return await client.ListMessagesAsync(query, maxResults, ct);
    }

    public async Task<string> ReadMessageAsync(string messageId, CancellationToken ct = default)
    {
        var client = await gmailApiClientFactory.CreateAsync(Self.AsScope(), ct);
        return await client.ReadMessageAsync(messageId, ct);
    }

    public async Task SendMessageAsync(string to, string subject, string body, CancellationToken ct = default)
    {
        var client = await gmailApiClientFactory.CreateAsync(Self.AsScope(), ct);
        await client.SendMessageAsync(to, subject, body, ct);
    }
}
