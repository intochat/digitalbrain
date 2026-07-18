using System;
using System.Text.Json;
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

[GrainType("DigitalBrain.SDK.Ai.LlmAlertingNeuron")]
[ImplicitStreamSubscription(LlmAlertingNeuronType)]
internal sealed class LlmAlertingNeuron(
    [FromKeyedServices("incoming")] IDurableList<Synapse> incoming,
    [FromKeyedServices("outgoing")] IDurableList<Synapse> outgoing,
    ISecretVault vault,
    IGrainFactory grains,
    ILogger<LlmAlertingNeuron> log)
    : Neuron(incoming, outgoing, grains, log),
      INeuronMetadata,
      IHandle<TextTranslatedEvent>
{
    public const string LlmAlertingNeuronType = nameof(LlmAlertingNeuron);

    public static NeuronId Id => new("ai/llm-alerting");
    public static string Icon => "warning";
    public static NeuronCapability Capabilities => NeuronCapability.Balanced;

    private IChatClient? _chat;

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);

        string? apiKey = null;
        try
        {
            // Decrypt "grok-api-key" as the primary naming convention, fallback to "xai-api-key"
            apiKey = await vault.DecryptSecretAsync("grok-api-key", cancellationToken);
            if (string.IsNullOrEmpty(apiKey))
            {
                apiKey = await vault.DecryptSecretAsync("xai-api-key", cancellationToken);
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to decrypt grok-api-key/xai-api-key in LlmAlertingNeuron, falling back.");
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
                Logger.LogInformation("Initializing live GrokConnector for LlmAlertingNeuron...");
                _chat = new GrokConnector(apiKey, "grok-beta");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to initialize GrokConnector in LlmAlertingNeuron.");
            }
        }
        else
        {
            Logger.LogInformation("LlmAlertingNeuron active key unresolved or mock. Fallback mock sentiment analysis active.");
        }
    }

    public async Task HandleAsync(TextTranslatedEvent synapse, CancellationToken cancellationToken)
    {
        Logger.LogInformation("LlmAlertingNeuron evaluating sentiment for translated text: {TranslatedText}", synapse.TranslatedText);

        string severity = "Info";
        string summary = $"Text translated: {synapse.TranslatedText}";

        if (_chat != null)
        {
            try
            {
                var prompt = @"Analyze the sentiment and severity of the following text.
Respond with a single JSON object. Do not include markdown fences:
{
  ""severity"": ""Info"" | ""Warning"" | ""Critical"",
  ""summary"": ""A brief 1-sentence summary of the translation and its emotional impact""
}
Text to analyze: """ + synapse.TranslatedText + @"""";
                var messages = new[] { new ChatMessage(ChatRole.User, prompt) };
                var response = await _chat.GetResponseAsync(messages, cancellationToken: cancellationToken);
                var text = response.Text?.Trim() ?? "";

                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;
                if (root.TryGetProperty("severity", out var sevProp))
                {
                    severity = sevProp.GetString() ?? "Info";
                }
                if (root.TryGetProperty("summary", out var sumProp))
                {
                    summary = sumProp.GetString() ?? summary;
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to analyze sentiment using live LLM. Falling back to heuristic sentiment analysis.");
            }
        }

        // Heuristic fallback if live LLM analysis failed or is mock
        if (severity == "Info" && summary == $"Text translated: {synapse.TranslatedText}")
        {
            var lowerText = synapse.TranslatedText.ToLowerInvariant();
            if (lowerText.Contains("hostile") || lowerText.Contains("danger") || lowerText.Contains("kill") || lowerText.Contains("attack") || lowerText.Contains("critical"))
            {
                severity = "Critical";
                summary = $"Critical hostile sentiment detected: \"{synapse.TranslatedText}\"";
            }
            else if (lowerText.Contains("alert") || lowerText.Contains("warning") || lowerText.Contains("bad") || lowerText.Contains("error"))
            {
                severity = "Warning";
                summary = $"Potential risk identified in text: \"{synapse.TranslatedText}\"";
            }
            else
            {
                severity = "Info";
                summary = $"Standard benign translation completed successfully.";
            }
        }

        var systemAlert = new SystemAlertFiredEvent(
            Severity: severity,
            AlertSummary: summary
        );

        // Broadcast this event to all receivers (GatewayNeuron/Timeline)
        var headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: synapse.Headers.CorrelationId,
            causationId: synapse.Headers.SynapseId.Value,
            callerNeuronId: InstanceId,
            callerNeuronType: LlmAlertingNeuronType,
            receiverNeuronId: default,
            receiverNeuronType: "GatewayNeuron",
            timestamp: DateTimeOffset.UtcNow
        );

        await FireSynapseAsync(systemAlert with { Headers = headers }, cancellationToken);

        // Render an interactive UI card representing this alert!
        var tone = severity == "Critical" ? "red" : (severity == "Warning" ? "amber" : "indigo");
        var renderData = new System.Text.Json.Nodes.JsonObject
        {
            ["title"] = $"{severity} System Alert Fired",
            ["body"] = $"{summary} (Language: {synapse.TargetLanguage})",
            ["initials"] = severity[..1].ToUpperInvariant(),
            ["tone"] = tone
        };

        await RenderAsync("digitalbrain", "sample_neuron", renderData, cancellationToken);
    }
}
