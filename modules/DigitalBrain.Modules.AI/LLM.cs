using DigitalBrain.Kernel;
using Microsoft.Extensions.AI;

namespace DigitalBrain.AI;

public abstract class LLM(IChatClient chatClient) : Neuron, ILLM
{
    public async Task<string> AskAsync(string prompt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        var response = await chatClient.GetResponseAsync(prompt);

        return response.Text;
    }
}
