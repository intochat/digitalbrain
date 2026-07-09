using DigitalBrain.Core;
using Microsoft.Extensions.AI;
namespace DigitalBrain.Kernel;

[GrainType("digitalbrain.llm.v1")]
public class LlmNeuron(ILogger<LlmNeuron> logger, NeuronJournals journals) : Neuron(logger, journals), ILlmNeuron
{
    public async Task HandleAsync(LlmPrompt prompt, CancellationToken cancellationToken = default)
    {
        var chat = ServiceProvider.GetService<IChatClient>();
        if (chat == null)
        {
            await FireAsync(new LlmResponse(prompt.Prompt, "[no local llm client]", "none"), cancellationToken);
            return;
        }

        var options = string.IsNullOrWhiteSpace(prompt.PreferredModel)
            ? null
            : new Microsoft.Extensions.AI.ChatOptions { ModelId = prompt.PreferredModel };
        var response = await chat.GetResponseAsync(prompt.Prompt, options, cancellationToken);
        await FireAsync(new LlmResponse(prompt.Prompt, response.Text.Trim(), prompt.PreferredModel ?? "llama3.1:8b"), cancellationToken);
    }
}


