using System;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DigitalBrain.Runtime;
using DigitalBrain.Runtime.Neurons;
using DigitalBrain.SDK.DigitalBrain.Ai;
using DigitalBrain.SDK.DigitalBrain.Security;
using DigitalBrain.SDK.XAI.Grok;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Journaling;

namespace DigitalBrain.SDK.DigitalBrain.Ai.LlmTranslation;

[GrainType("DigitalBrain.SDK.Ai.DocumentParserNeuron")]
[ImplicitStreamSubscription(DocumentParserNeuronType)]
internal sealed class DocumentParserNeuron(
    [FromKeyedServices("incoming")] IDurableList<Synapse> incoming,
    [FromKeyedServices("outgoing")] IDurableList<Synapse> outgoing,
    ISecretVault vault,
    IGrainFactory grains,
    ILogger<DocumentParserNeuron> log)
    : Neuron(incoming, outgoing, grains, log),
      INeuronMetadata,
      IHandle<ParseDocRequest>
{
    public const string DocumentParserNeuronType = nameof(DocumentParserNeuron);

    public static NeuronId Id => new("ai/document-parser");
    public static string Icon => "description";
    public static NeuronCapability Capabilities => NeuronCapability.Balanced;

    private IChatClient? _chat;

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);

        string? apiKey = null;
        try
        {
            apiKey = await vault.DecryptSecretAsync("grok-api-key", cancellationToken);
            if (string.IsNullOrEmpty(apiKey))
            {
                apiKey = await vault.DecryptSecretAsync("xai-api-key", cancellationToken);
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to decrypt grok-api-key/xai-api-key in DocumentParserNeuron, falling back.");
        }

        if (string.IsNullOrEmpty(apiKey) || apiKey == "placeholder")
        {
            apiKey = Environment.GetEnvironmentVariable("DigitalBrain__Ai__GrokApiKey")
                ?? Environment.GetEnvironmentVariable("XAI_API_KEY")
                ?? Environment.GetEnvironmentVariable("GROK_API_KEY")
                ?? Environment.GetEnvironmentVariable("grok-api-key");
        }

        if (!string.IsNullOrEmpty(apiKey) && apiKey != "mock-xai-api-key" && apiKey != "placeholder")
        {
            try
            {
                Logger.LogInformation("Initializing live GrokConnector for DocumentParserNeuron...");
                _chat = new GrokConnector(apiKey, "grok-beta");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to initialize GrokConnector in DocumentParserNeuron.");
            }
        }
        else
        {
            Logger.LogInformation("DocumentParserNeuron active key unresolved or mock. Fallback mock parsing active.");
        }
    }

    public async Task HandleAsync(ParseDocRequest synapse, CancellationToken cancellationToken)
    {
        Logger.LogInformation("DocumentParserNeuron parsing document: {DocName}", synapse.DocumentName);

        string conceptsJson = "";
        string overallSentiment = "Neutral";

        if (_chat != null)
        {
            try
            {
                var prompt = @"Analyze the following text from a document.
Respond with a single JSON object. Do not include markdown fences:
{
  ""concepts"": [""concept1"", ""concept2"", ""concept3""],
  ""sentiment"": ""Positive"" | ""Warning"" | ""Critical""
}
Text to analyze: """ + synapse.TextContent + @"""";

                var messages = new[] { new ChatMessage(ChatRole.User, prompt) };
                var response = await _chat.GetResponseAsync(messages, cancellationToken: cancellationToken);
                var text = response.Text?.Trim() ?? "";

                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;
                if (root.TryGetProperty("concepts", out var conceptsProp))
                {
                    conceptsJson = JsonSerializer.Serialize(conceptsProp);
                }
                if (root.TryGetProperty("sentiment", out var sentProp))
                {
                    overallSentiment = sentProp.GetString() ?? "Neutral";
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to parse document using live LLM. Falling back to mock parsing.");
            }
        }

        if (string.IsNullOrEmpty(conceptsJson))
        {
            // Heuristic mock parser extraction fallback
            var words = synapse.TextContent.Split(new[] { ' ', ',', '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries);
            var uniqueWords = new System.Collections.Generic.HashSet<string>();
            foreach (var w in words)
            {
                if (w.Length > 4 && char.IsUpper(w[0]))
                {
                    uniqueWords.Add(w);
                    if (uniqueWords.Count >= 5) break;
                }
            }

            // Default concepts if not enough capitals
            if (uniqueWords.Count < 2)
            {
                uniqueWords.Add("Architecture");
                uniqueWords.Add("Pipeline");
                uniqueWords.Add("VisualConstructor");
            }

            conceptsJson = JsonSerializer.Serialize(uniqueWords);

            var lowerText = synapse.TextContent.ToLowerInvariant();
            if (lowerText.Contains("hostile") || lowerText.Contains("danger") || lowerText.Contains("critical"))
            {
                overallSentiment = "Critical";
            }
            else if (lowerText.Contains("warning") || lowerText.Contains("alert") || lowerText.Contains("error"))
            {
                overallSentiment = "Warning";
            }
            else
            {
                overallSentiment = "Positive";
            }
        }

        Logger.LogInformation("DocumentParserNeuron finished extraction. Concepts: {ConceptsJson}, Sentiment: {Sentiment}", conceptsJson, overallSentiment);

        var extractedEvent = new ConceptsExtractedEvent(
            DocumentName: synapse.DocumentName,
            ConceptsJson: conceptsJson,
            OverallSentiment: overallSentiment
        );

        var headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: synapse.Headers.CorrelationId,
            causationId: synapse.Headers.SynapseId.Value,
            callerNeuronId: InstanceId,
            callerNeuronType: DocumentParserNeuronType,
            receiverNeuronId: default,
            receiverNeuronType: nameof(VisualCanvasNeuron),
            timestamp: DateTimeOffset.UtcNow
        );

        await FireSynapseAsync(extractedEvent with { Headers = headers }, cancellationToken);
    }
}
