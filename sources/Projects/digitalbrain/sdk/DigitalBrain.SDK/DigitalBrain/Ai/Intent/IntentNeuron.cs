using System.Text.Json;
using System.Text.RegularExpressions;
using DigitalBrain.Runtime.User;
using Microsoft.Extensions.AI;
using Orleans.Journaling;
using DigitalBrain.Runtime.Neurons;
using DigitalBrain.Runtime;
using DigitalBrain.SDK.DigitalBrain.Ai;
using DigitalBrain.SDK.DigitalBrain.Ai.Models;

namespace DigitalBrain.SDK.DigitalBrain.Ai.Intent;

[ImplicitStreamSubscription(IntentNeuronType)]
internal sealed class IntentNeuron(
    [FromKeyedServices("incoming")] IDurableList<Synapse> incoming,
    [FromKeyedServices("outgoing")] IDurableList<Synapse> outgoing,
    IGrainFactory grains,
    [Llm<Gpt5Mini>] IChatClient chat,
    ILogger<IntentNeuron> logger)
    : Neuron(incoming, outgoing, grains, logger),
      IIntent,
      INeuronMetadata,
      IExternalNeuron,
      IHandle<ClassifyIntentRequest>,
      IHandle<UserPromptReceived>
{
    public const string IntentNeuronType = nameof(IntentNeuron);

    public static NeuronId Id => new("ai/intent");
    public static string Icon => "openai";
    public static NeuronCapability Capabilities => NeuronCapability.Balanced;

    public const string SystemPrompt =
        "You classify a user utterance into one of these intents:\n" +
        "  - GetLastNGmailSenders: user wants to retrieve recent email senders.\n" +
        "    Slots: N (integer), DatabaseId (default \"email-senders\"), UserAccountId.\n" +
        "  - ExplainQuery: user asks a meta question about past behavior. Triggered by\n" +
        "    phrases like \"why did you\", \"what did I ask\", \"trace this\", \"explain X\",\n" +
        "    \"remember when I\", \"show me my recent\". Copy the original utterance\n" +
        "    verbatim into params[\"Query\"] and params[\"UserId\"] (default \"default\").\n" +
        "  - LifePlanning: user wants to organize their life across multiple areas\n" +
        "    (finances, health, time management, schedule, habits, productivity). Triggered\n" +
        "    by phrases like \"put my life in order\", \"plan my week\", \"help me organize\",\n" +
        "    \"sort out my <area>\". Copy the original utterance into params[\"Prompt\"].\n" +
        "  - FindVideo: user wants to find or play a video (e.g. \"find a youtube\n" +
        "    video about X\", \"play a video of Y\", \"show me a youtube clip of Z\").\n" +
        "    Copy the user's search intent (the topic they want to find a video\n" +
        "    about) into params[\"Query\"].\n" +
        "  - OpenCanvas: user wants to open a whiteboard / canvas / sketchpad,\n" +
        "    or show or draw a diagram (e.g. \"open my whiteboard\", \"let me\n" +
        "    sketch\", \"open the canvas called X\", \"show the diagram named X\",\n" +
        "    \"draw a diagram of Y\"). Put the scene name into params[\"SceneName\"]:\n" +
        "    the text after \"called\"/\"named\", or the subject after\n" +
        "    \"draw/show a diagram of\"; omit params[\"SceneName\"] (or use \"\") if\n" +
        "    there is no name.\n" +
        "  - CreateFolder: user wants to create a folder or directory. Triggered by phrases like\n" +
        "    \"create a folder\", \"make a directory\", \"create folder on X drive\". Copy the original\n" +
        "    utterance verbatim into params[\"Prompt\"].\n" +
        "  - NemoChat: user wants to chat with the local Nemo LLM. Triggered by general chat or prompts starting with nemo or chat. Copy prompt verbatim into params[\"Prompt\"].\n" +
        "  - Unknown: anything else.\n\n" +
        "Respond with strict JSON of the shape:\n" +
        "{\"intent\":\"GetLastNGmailSenders\",\"params\":{\"N\":\"5\",\"DatabaseId\":\"email-senders\"}}\n" +
        "For ExplainQuery: {\"intent\":\"ExplainQuery\",\"params\":{\"Query\":\"<verbatim utterance>\",\"UserId\":\"default\"}}\n" +
        "For LifePlanning: {\"intent\":\"LifePlanning\",\"params\":{\"Prompt\":\"<verbatim utterance>\"}}\n" +
        "For FindVideo: {\"intent\":\"FindVideo\",\"params\":{\"Query\":\"<search phrase>\"}}\n" +
        "For OpenCanvas: {\"intent\":\"OpenCanvas\",\"params\":{\"SceneName\":\"<name or empty>\"}}\n" +
        "For CreateFolder: {\"intent\":\"CreateFolder\",\"params\":{\"Prompt\":\"<verbatim utterance>\"}}\n" +
        "For NemoChat: {\"intent\":\"NemoChat\",\"params\":{\"Prompt\":\"<verbatim prompt>\"}}\n" +
        "Output ONLY the JSON object. No prose, no markdown fences.";

    protected override async Task HandleSynapseAsync(Synapse synapse)
    {
        switch (synapse)
        {
            case ClassifyIntentRequest req:
                await ClassifyAndFireAsync(req.Transcript, req.CorrelationId, req.CallerNeuronId, req.CallerNeuronType, req.SynapseId);
                break;
            case UserPromptReceived prompt:
                await ClassifyAndFireAsync(prompt.Text, prompt.CorrelationId, prompt.CallerNeuronId, prompt.CallerNeuronType, prompt.SynapseId);
                break;
        }
    }

    async Task ClassifyAndFireAsync(
        string transcript,
        Guid correlationId,
        Guid callerNeuronId,
        string? callerNeuronType,
        Guid causation)
    {
        var trimmed = transcript.Trim();

        // Deterministic fast-path for the widget-canvas demo intents. These are
        // single-shape utterances ("set a clock", "remind me in N minutes",
        // "show flight CODE") whose slots parse cleanly without an LLM round-trip,
        // so the panel appears even when the classifier model is mocked/offline.
        if (TryMatchWidgetCanvasIntent(trimmed, out var canvasIntent, out var canvasParams))
        {
            await FireSynapseAsync(new IntentClassified(Transcript: transcript,
            Intent: canvasIntent,
            Parameters: canvasParams) { Headers = SynapseMetadata.Create(
                synapseId: Guid.NewGuid(),
                correlationId: correlationId,
                causationId: causation,
                callerNeuronId: default,
                callerNeuronType: null,
                receiverNeuronId: callerNeuronId,
                receiverNeuronType: callerNeuronType ?? "External",
                timestamp: default
            ) });
            return;
        }

        if (trimmed.StartsWith("nemo ", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("chat ", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "nemo", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "chat", StringComparison.OrdinalIgnoreCase))
        {
            var promptVal = trimmed;
            if (trimmed.StartsWith("nemo ", StringComparison.OrdinalIgnoreCase))
                promptVal = trimmed.Substring(5).Trim();
            else if (trimmed.StartsWith("chat ", StringComparison.OrdinalIgnoreCase))
                promptVal = trimmed.Substring(5).Trim();

            await FireSynapseAsync(new IntentClassified(Transcript: transcript,
            Intent: KnownIntent.NemoChat,
            Parameters: new Dictionary<string, string> { ["Prompt"] = promptVal }) { Headers = SynapseMetadata.Create(
                synapseId: Guid.NewGuid(),
                correlationId: correlationId,
                causationId: causation,
                callerNeuronId: default,
                callerNeuronType: null,
                receiverNeuronId: callerNeuronId,
                receiverNeuronType: callerNeuronType ?? "External",
                timestamp: default
            ) });
            return;
        }

        var response = await chat.GetResponseAsync(
            [
                new ChatMessage(ChatRole.System, SystemPrompt),
                new ChatMessage(ChatRole.User, transcript),
            ],
            new ChatOptions { MaxOutputTokens = 200 });

        var (intent, parameters) = ParseIntentJson(response.Text ?? string.Empty);

        await FireSynapseAsync(new IntentClassified(Transcript: transcript,
        Intent: intent,
        Parameters: parameters) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: correlationId,
            causationId: causation,
            callerNeuronId: default,
            callerNeuronType: null,
            receiverNeuronId: callerNeuronId,
            receiverNeuronType: callerNeuronType ?? "External",
            timestamp: default
        ) });
    }

    static readonly Regex SetClockPattern =
        new(@"^set\s+(?:a|an|the)?\s*clock\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex RemindPattern =
        new(@"\bremind\s+me\b.*?(\d+)\s*(?:minute|min)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex ShowFlightPattern =
        new(@"\b(?:show|track|display)\b.*?\bflight\s+([A-Za-z]{1,3}\s?\d{1,4}[A-Za-z]?)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    static bool TryMatchWidgetCanvasIntent(
        string transcript,
        out KnownIntent intent,
        out IReadOnlyDictionary<string, string> parameters)
    {
        var remind = RemindPattern.Match(transcript);
        if (remind.Success)
        {
            intent = KnownIntent.RemindMe;
            parameters = new Dictionary<string, string>(StringComparer.Ordinal) { ["Minutes"] = remind.Groups[1].Value };
            return true;
        }

        var flight = ShowFlightPattern.Match(transcript);
        if (flight.Success)
        {
            intent = KnownIntent.ShowFlight;
            parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Code"] = flight.Groups[1].Value.Replace(" ", "").ToUpperInvariant(),
            };
            return true;
        }

        if (SetClockPattern.IsMatch(transcript))
        {
            intent = KnownIntent.SetClock;
            parameters = new Dictionary<string, string>(StringComparer.Ordinal) { ["Timezone"] = "local" };
            return true;
        }

        intent = KnownIntent.Unknown;
        parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        return false;
    }

    static (KnownIntent Intent, IReadOnlyDictionary<string, string> Parameters) ParseIntentJson(string text)
    {
        var empty = (KnownIntent.Unknown, (IReadOnlyDictionary<string, string>)new Dictionary<string, string>());
        if (string.IsNullOrWhiteSpace(text)) return empty;

        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            var intent = root.TryGetProperty("intent", out var intentEl)
                && Enum.TryParse<KnownIntent>(intentEl.GetString(), ignoreCase: false, out var parsed)
                    ? parsed
                    : KnownIntent.Unknown;

            var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
            if (root.TryGetProperty("params", out var paramsEl) && paramsEl.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in paramsEl.EnumerateObject())
                    parameters[prop.Name] = prop.Value.ToString();
            }
            return (intent, parameters);
        }
        catch (JsonException)
        {
            return empty;
        }
    }
}
