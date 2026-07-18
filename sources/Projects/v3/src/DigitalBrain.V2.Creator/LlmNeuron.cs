using DigitalBrain.V2.Core.Runtime;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.V2.Creator;

public sealed class LlmNeuron : Neuron, ILlmNeuron
{
    private readonly IChatClient _chatClient;

    public LlmNeuron([FromKeyedServices("v2-creator-llm")] IChatClient chatClient)
    {
        _chatClient = chatClient;
    }

    public async Task HandleAsync(LlmPrompt synapse, CancellationToken ct)
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, synapse.SystemPrompt),
            new(ChatRole.User, synapse.UserPrompt)
        };

        InoDraft draft;
        try
        {
            var response = await _chatClient.GetResponseAsync<InoDraft>(
                messages,
                new ChatOptions(),
                useJsonSchemaResponseFormat: true,
                cancellationToken: ct);

            draft = response.Result;
        }
        catch (Exception ex)
        {
            await Reply(new LlmCompletion(synapse.Capability, string.Empty, [ex.Message]));
            return;
        }

        var ino = (draft?.InoSource ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(ino))
        {
            await Reply(new LlmCompletion(synapse.Capability, string.Empty, ["LLM returned empty inoSource"]));
            return;
        }

        await Reply(new LlmCompletion(synapse.Capability, ino, []));
    }

    public Task HandleAsync(NeuronAuthored _, CancellationToken ct) => Task.CompletedTask;
}