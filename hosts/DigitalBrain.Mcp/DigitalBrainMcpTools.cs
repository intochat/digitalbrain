using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using DigitalBrain.AI.Ollama;
using DigitalBrain.Client;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;

namespace DigitalBrain.Mcp;

[McpServerToolType]
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Constructed by the MCP server DI container via WithTools<DigitalBrainMcpTools>().")]
internal sealed class DigitalBrainMcpTools(IDigitalBrain brain)
{
    [McpServerTool(Name = McpHost.AskLlama32ToolName)]
    [Description("Ask the owner's Llama 3.2 neuron and return its ChatResponse.")]
    public Task<ChatResponse> AskLlama32Async(
        [Description("User prompt for Llama 3.2")] string prompt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        return brain.Get<ILlama32>(McpHost.DefaultLlama32Key).Respond(
            [new ChatMessage(ChatRole.User, prompt)]);
    }
}
