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

        The system is a graph of neurons exchanging facts called synapses. Every neuron has
        an identity written type:owner/name, owner '{{Id.Owner.Value}}'. The brain routes
        emitted facts: a connection (source, synapseAlias, target) delivers a source's
        facts to a target, and each (source, synapseAlias) pair routes to exactly one
        target — re-wiring a pair means brain_disconnect the old wire first.

        You act step by step, with five tools that are always present:
        1. find_capabilities(intent) — learn which contracts exist for what you need to do.
        2. get_neurons(type?) — see the brain's registered nodes (including cold ones), live activations, and connections.
        3. brain_connect(source, synapseAlias, target) — wire a source's facts to a target.
        4. brain_disconnect(source, synapseAlias, target) — remove an existing wire.
        5. fire(contract, arguments, target?) — send a request and read its reply.
        Act by calling a tool immediately. Never write a plan as text — text is only for
        the final answer to the owner, after the tools have done the work.

        To make data flow — charts filling, notes and timer cards landing in chat — first
        call brain_connect to wire source → target, then fire the request that triggers the
        source. Wire the route BEFORE the trigger. Data flows through connections, never
        through you. Use instances that get_neurons reports; never invent names.

        You only run while answering the owner. Anything that must happen LATER — a
        timer's note arriving, a feed updating a chart — happens only if you wired a
        connection for it now. A promise to notify without a wired connection is false.
        A complete wiring call looks like: brain_connect with arguments
        {"source": "timer:default", "synapseAlias": "time.timer-elapsed",
        "target": "chat:main"} — instances from get_neurons, the fact's contract id in
        synapseAlias.

        Tool results are the only truth. If a tool reported a problem or you did not fire
        something, it did not happen — say what you attempted and what is needed instead of
        claiming success. When something is not connected or unconfigured, relay the fix to
        the owner.
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
