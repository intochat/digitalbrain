using DigitalBrain.Runtime.Introspector;
using Microsoft.Extensions.AI;
using Orleans.Journaling;
using System.Text.RegularExpressions;
using DigitalBrain.Runtime.Neurons;
using DigitalBrain.Runtime;
using DigitalBrain.SDK.DigitalBrain.Ai.Models;

namespace DigitalBrain.SDK.DigitalBrain.Ai.Explaining;

[ImplicitStreamSubscription(ExplainerNeuronType)]
internal sealed class ExplainerNeuron(
    [FromKeyedServices("incoming")] IDurableList<Synapse> incoming,
    [FromKeyedServices("outgoing")] IDurableList<Synapse> outgoing,
    [Llm<Gpt5>] IChatClient chat,
    IGrainFactory grains,
    TimeProvider time,
    ILogger<ExplainerNeuron> log)
    : Neuron(incoming, outgoing, grains, log),
      IExplainer, INeuronMetadata, IHandle<ExplainerRequest>
{
    public const string ExplainerNeuronType = nameof(ExplainerNeuron);

    public static NeuronId         Id           => new("ai/explainer");
    public static string           Icon         => "explainer";
    public static NeuronCapability Capabilities => NeuronCapability.Reasoning;

    private const string SystemPrompt = """
        You are DigitalBrain's Explainer. Use the introspector tools to ground your answers in
        real synapse chains. When you reference a past decision, cite its correlation id
        in a <cite>guid</cite> tag (downstream extracts these). Be concise — 1-3 sentences.
        """;

    protected override async Task HandleSynapseAsync(Synapse s)
    {
        if (s is not ExplainerRequest req) return;

        var introspector = Grains.GetGrain<IIntrospector>(Guid.Empty);

        var options = new ChatOptions
        {
            Tools =
            [
                AIFunctionFactory.Create(
                    (string text) =>
                        introspector.FindChainsByConversationTextAsync(text, since: null, until: null, limit: 10, default),
                    name: "find_chains_with_text",
                    description: "Search past user prompts by literal text. Returns matching correlation ids."),

                AIFunctionFactory.Create(
                    (Guid correlationId) =>
                        introspector.TraceCorrelationAsync(correlationId, default),
                    name: "trace_correlation",
                    description: "Get the full synapse chain for a correlation id."),

                AIFunctionFactory.Create(
                    (double sinceHours) =>
                        introspector.GetRecentActivityAsync(req.UserId, TimeSpan.FromHours(sinceHours), default),
                    name: "recent_activity",
                    description: "List correlation ids the user initiated recently (sinceHours = hours to look back)."),

                AIFunctionFactory.Create(
                    (string query) =>
                        introspector.FindNeuronsByFeatureTextAsync(query, limit: 10, default),
                    name: "find_neurons_with_feature",
                    description: "Find neurons whose .feature text matches a literal query."),
            ],
        };

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, SystemPrompt),
            new(ChatRole.User, req.NaturalLanguageQuery),
        };

        var response = await chat.GetResponseAsync(messages, options);
        var answerText = response.Text ?? "I couldn't find a related chain.";
        var cited = ExtractCitedCorrelationIds(answerText);

        await FireSynapseAsync(new ExplainerResponse(NaturalLanguageAnswer: answerText,
        CitedCorrelationIds:   cited,
        ToolCallTrace:         Array.Empty<string>()) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: req.CorrelationId,
            causationId: req.SynapseId,
            callerNeuronId: InstanceId,
            callerNeuronType: nameof(ExplainerNeuron),
            receiverNeuronId: req.CallerNeuronId,
            receiverNeuronType: req.CallerNeuronType ?? "IntrospectorNeuron",
            timestamp: time.GetUtcNow()
        ) });
    }

    private static readonly Regex CitePattern =
        new(@"<cite>([0-9a-fA-F-]+)</cite>", RegexOptions.Compiled);

    internal static IReadOnlyList<Guid> ExtractCitedCorrelationIds(string text) =>
        CitePattern.Matches(text)
            .Select(m => Guid.TryParse(m.Groups[1].Value, out var g) ? g : Guid.Empty)
            .Where(g => g != Guid.Empty)
            .Distinct()
            .ToArray();
}
