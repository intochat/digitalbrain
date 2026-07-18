namespace DigitalBrain.AI;

using System.Text.Json;
using Brain.Contracts;
using Brain.Kernel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Journaling;

[GrainType("agent.gpt56.v1")]
public sealed class Gpt56Neuron(
    [FromKeyedServices("gpt56-receipts")] IDurableDictionary<string, CommandReceipt> receipts,
    [FromKeyedServices("gpt56-events")] IDurableDictionary<string, byte> processedEvents,
    [FromKeyedServices("gpt56-sequences")] IDurableDictionary<string, long> sourceSequences,
    [FromKeyedServices("gpt56-outbox")] IDurableList<OutboxIntent<string>> outbox,
    [FromKeyedServices("gpt56-domain")] IDurableDictionary<string, string> domain,
    [FromKeyedServices("gpt56-flags")] IDurableDictionary<string, string> flags,
    [FromKeyedServices("gpt56-failures")] IDurableList<SanitizedFailure> failures,
    [FromKeyedServices("gpt56-accepted-causation")] IDurableDictionary<string, byte> acceptedCausation,
    [FromKeyedServices("gpt56-rejected-causation")] IDurableDictionary<string, byte> rejectedCausation,
    [FromKeyedServices(AiServiceKeys.Gpt56ChatClient)] IChatClient chatClient,
    IOptions<AiProviderOptions> options) : ReactiveNeuron<string>(
        receipts,
        processedEvents,
        sourceSequences,
        outbox,
        domain,
        flags,
        failures,
        acceptedCausation,
        rejectedCausation), IGpt56Turn
{
    private readonly IChatClient _chatClient = chatClient;
    private readonly AiProviderOptions _options = options.Value;

    public Task<string> GetIdentityAsync() => Task.FromResult(this.GetPrimaryKeyString());

    public async Task<AgentTurnResult> CompleteTurnAsync(CommandSynapse<AgentTurnRequest> command)
    {
        string? responseText = null;
        var receipt = await ExecuteCommandCoreAsync(command, async (payload, commit) =>
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken.None);
            timeout.CancelAfter(_options.ProviderTimeout);

            ChatResponse response;
            try
            {
                response = await _chatClient.GetResponseAsync(
                    [new ChatMessage(ChatRole.User, payload.InputText)],
                    cancellationToken: timeout.Token);
            }
            catch (Exception)
            {
                throw new BrainException(BrainErrors.FailureSanitized, ReactiveNeuronPipeline<string>.UnknownFailureMessage);
            }

            responseText = response.Messages.LastOrDefault(m => !string.IsNullOrWhiteSpace(m.Text))?.Text
                ?? string.Empty;
            var prior = ReadState();
            var next = new AgentDomainState(payload.RequestId, responseText, prior.CompletedTurnCount + 1);
            await commit(new ReactiveCommit<string>(
                JsonSerializer.Serialize(next),
                UiRevision: CurrentRevision + 1,
                Outbox: []));
            return CommandReceiptStatus.Accepted;
        });

        if (receipt.Status != CommandReceiptStatus.Accepted)
        {
            throw new BrainException(
                receipt.FailureCode ?? BrainErrors.FailureSanitized,
                receipt.FailureMessage ?? ReactiveNeuronPipeline<string>.UnknownFailureMessage);
        }

        var state = ReadState();
        return new AgentTurnResult(
            command.Payload.RequestId,
            responseText ?? state.LastResponseText ?? string.Empty,
            receipt.Revision);
    }

    public Task<AgentTurnStateSnapshot> GetTurnStateAsync()
    {
        var state = ReadState();
        return Task.FromResult(new AgentTurnStateSnapshot(
            this.GetPrimaryKeyString(),
            state.LastRequestId,
            state.CompletedTurnCount,
            CurrentRevision));
    }

    private AgentDomainState ReadState()
    {
        if (string.IsNullOrWhiteSpace(DomainState))
            return new AgentDomainState(null, null, 0);

        try
        {
            return JsonSerializer.Deserialize<AgentDomainState>(DomainState) ?? new AgentDomainState(null, null, 0);
        }
        catch (JsonException)
        {
            return new AgentDomainState(null, null, 0);
        }
    }

    private sealed record AgentDomainState(string? LastRequestId, string? LastResponseText, int CompletedTurnCount);
}
