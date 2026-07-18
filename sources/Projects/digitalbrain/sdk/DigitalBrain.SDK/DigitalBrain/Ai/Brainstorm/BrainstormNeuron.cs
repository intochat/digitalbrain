using System.Text.Json;
using DigitalBrain.Runtime.Ui;
using Microsoft.Extensions.AI;
using Orleans.Journaling;
using DigitalBrain.Runtime.Neurons;
using DigitalBrain.Runtime;
using DigitalBrain.SDK.DigitalBrain.Ai;
using DigitalBrain.SDK.DigitalBrain.Ai.Models;

namespace DigitalBrain.SDK.DigitalBrain.Ai.Brainstorm;

[ImplicitStreamSubscription(BrainstormNeuronType)]
internal sealed class BrainstormNeuron(
    [FromKeyedServices("incoming")] IDurableList<Synapse> incoming,
    [FromKeyedServices("outgoing")] IDurableList<Synapse> outgoing,
    [Llm<Gpt5>] IChatClient chat,
    IGrainFactory grains,
    TimeProvider time,
    ILogger<BrainstormNeuron> log)
    : Neuron(incoming, outgoing, grains, log),
      INeuronMetadata, IHandle<BrainstormRequest>
{
    public const string BrainstormNeuronType = nameof(BrainstormNeuron);

    public static NeuronId         Id           => new("ai/brainstorm");
    public static string           Icon         => "brainstorm";
    public static NeuronCapability Capabilities => NeuronCapability.Reasoning;

    internal const string SystemPrompt = """
        You are DigitalBrain's Brainstormer. The user has typed a broad goal. Your
        job is to enumerate distinct DIRECTIONS the user could take, NOT to
        answer the question itself. Each direction is a different angle of
        attack — different specialist neurons, different first step, different
        trade-offs.

        Return ONLY a JSON array of 2 to 4 option objects, no surrounding
        prose, no markdown fences:

        [
          {
            "id":           "<short-kebab-slug>",
            "title":        "<short, action-oriented label, 3-6 words>",
            "summary":      "<one sentence, plain English, explains the angle>",
            "participants": ["TimeManager", "FinancialAdvisor", ...]
          },
          ...
        ]

        Participants are specialist neurons that would join a group chat to
        flesh out that direction. Common specialists you may name:
          - TimeManager — weekly schedule, time-boxing
          - FinancialAdvisor — budgeting, expense tracking
          - DietSpecialist — meal plans, nutrition
          - GoogleCalendar — reading and writing calendar events
          - Gmail — reading recent emails for context
          - Navigator — explaining past decisions

        Pick 2-4 participants per option that match the option's angle. Options
        should be MEANINGFULLY different from each other.
        """;

    protected override async Task HandleSynapseAsync(Synapse s)
    {
        if (s is not BrainstormRequest req) return;

        var minOptions = req.MinOptions is >= 2 and <= 4 ? req.MinOptions : 2;
        var maxOptions = req.MaxOptions is >= 2 and <= 4 ? req.MaxOptions : 4;
        if (maxOptions < minOptions) maxOptions = minOptions;

        var userPrompt =
            $"User goal:\n{req.Prompt}\n\n" +
            $"Return between {minOptions} and {maxOptions} options.";

        var response = await chat.GetResponseAsync(
            [
                new ChatMessage(ChatRole.System, SystemPrompt),
                new ChatMessage(ChatRole.User,   userPrompt),
            ],
            new ChatOptions());

        var options = ParseOptions(response.Text ?? "", minOptions, maxOptions);

        await FireSynapseAsync(new BrainstormOptions(Prompt:             req.Prompt,
        Options:            options) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: req.CorrelationId,
            causationId: req.SynapseId,
            callerNeuronId: InstanceId,
            callerNeuronType: BrainstormNeuronType,
            receiverNeuronId: req.CallerNeuronId,
            receiverNeuronType: req.CallerNeuronType ?? "External",
            timestamp: time.GetUtcNow()
        ) });

        var cardJson = JsonSerializer.Serialize(new
        {
            prompt  = req.Prompt,
            options = options.Select(o => new
            {
                id           = o.Id,
                title        = o.Title,
                summary      = o.Summary,
                participants = o.Participants,
            }).ToArray(),
        });
        await FireSynapseAsync(new RfwCard(LibraryName:        "digitalbrain",
        RootWidget:         "OptionChipStackCard",
        DataJson:           cardJson) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: req.CorrelationId,
            causationId: req.SynapseId,
            callerNeuronId: InstanceId,
            callerNeuronType: BrainstormNeuronType,
            receiverNeuronId: Guid.Empty,
            receiverNeuronType: "HomeFeed",
            timestamp: time.GetUtcNow()
        ) });
    }

    internal static IReadOnlyList<BrainstormOption> ParseOptions(string text, int min, int max)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<BrainstormOption>();

        var json = ExtractJsonArray(text);
        if (json is null) return Array.Empty<BrainstormOption>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return Array.Empty<BrainstormOption>();

            var result = new List<BrainstormOption>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (result.Count >= max) break;

                var id           = el.TryGetProperty("id",      out var idEl)      ? idEl.GetString()      ?? "" : "";
                var title        = el.TryGetProperty("title",   out var titleEl)   ? titleEl.GetString()   ?? "" : "";
                var summary      = el.TryGetProperty("summary", out var summaryEl) ? summaryEl.GetString() ?? "" : "";

                var participants = new List<string>();
                if (el.TryGetProperty("participants", out var partsEl) && partsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var p in partsEl.EnumerateArray())
                    {
                        var s = p.GetString();
                        if (!string.IsNullOrWhiteSpace(s)) participants.Add(s);
                    }
                }

                if (string.IsNullOrWhiteSpace(id))    id    = $"option-{result.Count + 1}";
                if (string.IsNullOrWhiteSpace(title)) title = $"Option {result.Count + 1}";

                result.Add(new BrainstormOption(id, title, summary, participants));
            }

            return result.Count >= min ? result : Array.Empty<BrainstormOption>();
        }
        catch (JsonException)
        {
            return Array.Empty<BrainstormOption>();
        }
    }

    static string? ExtractJsonArray(string text)
    {
        var start = text.IndexOf('[');
        var end   = text.LastIndexOf(']');
        return (start >= 0 && end > start) ? text[start..(end + 1)] : null;
    }
}
