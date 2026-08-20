using System.ComponentModel;
using DigitalBrain.Abstractions;
using DigitalBrain.AI;
using DigitalBrain.Core;
using DigitalBrain.AI.Ollama;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Assistant;
internal sealed class Assistant([FromKeyedServices(typeof(Gemma4))] IChatClient chatClient)
    : Agent(chatClient), IAssistant
{
    protected override string? Instructions =>
        $$"""
        You are DigitalBrain, the AI assistant inside the owner's DigitalBrain.

        The system has typed neurons exchanging requests and journaled facts. Every neuron has
        an identity written type:owner/name, owner '{{Id.Owner.Value}}'.

        You act step by step, with three tools that are always present:
        1. find_capabilities(intent) — learn which contracts exist for what you need to do.
        2. get_neurons(type?) — see live activations.
        3. fire(contract, arguments, target?) — send a request and read its reply.
        Act by calling a tool immediately. Never write a plan as text — text is only for
        the final answer to the owner, after the tools have done the work.

        Tool results are the only truth. If a tool reported a problem or you did not fire
        something, it did not happen — say what you attempted and what is needed instead of
        claiming success. When something is unconfigured, relay the fix to the owner.
        """;

    protected override IReadOnlyList<AIFunction> AdditionalToolsFor(IReadOnlyList<ChatMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var tools = new List<AIFunction>();

        if (LatestOwnerText(messages).Contains("llama", StringComparison.OrdinalIgnoreCase))
        {
            tools.Add(AskLlamaTool());
        }

        return tools;
    }

    private AIFunction AskLlamaTool()
        => Capability(
            "ask_llama",
            "Ask the local Llama model one question and return its answer. Offered only "
            + "because the owner named llama; never consult it on your own.",
            ([Description("The single question for Llama")] string question)
                => AskLlamaAsync(question));

    private async Task<string> AskLlamaAsync(string question)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);

        var llama = ServiceProvider.GetRequiredKeyedService<IChatClient>(typeof(Llama32));
        var answered = await llama
            .GetResponseAsync([new ChatMessage(ChatRole.User, question)])
            .ConfigureAwait(true);

        return string.IsNullOrWhiteSpace(answered.Text)
            ? "Llama returned no answer."
            : answered.Text;
    }

    private static string LatestOwnerText(IReadOnlyList<ChatMessage> messages)
    {
        for (var turn = messages.Count - 1; turn >= 0; turn--)
        {
            if (messages[turn].Role == ChatRole.User)
            {
                return messages[turn].Text;
            }
        }

        return string.Empty;
    }
}
