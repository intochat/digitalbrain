using Core.Contracts;
using IAW.Core;
using Ino.Core;
using Ino.Core.Capabilities;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Journaling;

namespace Ino.Core.Hosting;

public sealed class InoNeuron(
    [AgentState] AgentDurableState durableState,
    IChatClient chatClient,
    [FromKeyedServices("journal")] IDurableList<EventEnvelope<InoJournalEvent>> journal,
    ICortexCapability cortex,
    IFirePort firePort,
    ILogger<InoNeuron> log)
    : LlmNeuron<InoJournalEvent>(durableState, chatClient, journal), IInoNeuron
{
    public async Task<InoResponse> AskAsync(string prompt, string correlationId, CancellationToken ct)
    {
        var (userId, sessionId) = InoNeuronGrainKey.Parse(this.GetPrimaryKeyString());

        var ctx = new NeuronContext(
            SynapseId: SynapseId.New(),
            CorrelationId: new CorrelationId(correlationId),
            Source: new Caller.Ambient(DomainId.From("kernel")),
            SourceStream: new StreamKey($"ino:{userId}/{sessionId}"),
            UserId: userId)
        {
            FirePort = firePort,
            Logger = log,
        };

        await RaiseAsync(new InoAsked(prompt, sessionId, DateTimeOffset.UtcNow), ctx, ct);

        var routing = await cortex.RouteAsync(prompt, ctx, ct);

        // TODO C.2: thread RoutedNeuronId through NeuronResult
        await RaiseAsync(
            new InoRouted(
                prompt,
                NeuronId: string.Empty,
                Source: routing.Source,
                DateTimeOffset.UtcNow),
            ctx, ct);

        return new InoResponse(
            Text: routing.Outcome.Message ?? "(no reply)",
            CorrelationId: correlationId,
            Rfw: routing.Outcome.Rfw,
            Success: routing.Outcome.Success,
            Source: routing.Source.ToString());
    }
}
