using System;
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

[GrainType("DigitalBrain.SDK.Ai.LlmTranslationNeuron")]
[ImplicitStreamSubscription(LlmTranslationNeuronType)]
internal sealed class LlmTranslationNeuron(
    [FromKeyedServices("incoming")] IDurableList<Synapse> incoming,
    [FromKeyedServices("outgoing")] IDurableList<Synapse> outgoing,
    ISecretVault vault,
    IGrainFactory grains,
    ILogger<LlmTranslationNeuron> log)
    : Neuron(incoming, outgoing, grains, log),
      INeuronMetadata,
      IHandle<TranslateTextRequest>
{
    public const string LlmTranslationNeuronType = nameof(LlmTranslationNeuron);

    public static NeuronId Id => new("ai/llm-translation");
    public static string Icon => "translate";
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
            Logger.LogWarning(ex, "Failed to decrypt grok-api-key/xai-api-key in LlmTranslationNeuron, falling back.");
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
                Logger.LogInformation("Initializing live GrokConnector for LlmTranslationNeuron...");
                _chat = new GrokConnector(apiKey, "grok-beta");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to initialize GrokConnector in LlmTranslationNeuron.");
            }
        }
        else
        {
            Logger.LogInformation("LlmTranslationNeuron active key unresolved or mock. Fallback mock translation active.");
        }
    }

    public async Task HandleAsync(TranslateTextRequest synapse, CancellationToken cancellationToken)
    {
        Logger.LogInformation("LlmTranslationNeuron processing translation request: {Text} -> {TargetLanguage}", synapse.Text, synapse.TargetLanguage);

        string translatedText = "";

        if (_chat != null)
        {
            try
            {
                var prompt = $"Translate the following text into {synapse.TargetLanguage}. Respond ONLY with the translation, no explanation, no markdown: \"{synapse.Text}\"";
                var messages = new[] { new ChatMessage(ChatRole.User, prompt) };
                var response = await _chat.GetResponseAsync(messages, cancellationToken: cancellationToken);
                translatedText = response.Text?.Trim() ?? "";
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to call live LLM for translation. Falling back to mock translation.");
            }
        }

        if (string.IsNullOrEmpty(translatedText))
        {
            // Mock Translation Fallback
            translatedText = $"[{synapse.TargetLanguage}] {synapse.Text}";
        }

        var translatedEvent = new TextTranslatedEvent(
            OriginalText: synapse.Text,
            TranslatedText: translatedText,
            TargetLanguage: synapse.TargetLanguage
        );

        // Set headers to route point-to-point to LlmAlertingNeuron
        var headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: synapse.Headers.CorrelationId,
            causationId: synapse.Headers.SynapseId.Value,
            callerNeuronId: InstanceId,
            callerNeuronType: LlmTranslationNeuronType,
            receiverNeuronId: default, // Let Orleans implicit stream subscription handle routing by type
            receiverNeuronType: nameof(LlmAlertingNeuron),
            timestamp: DateTimeOffset.UtcNow
        );

        // We can fire it!
        await FireSynapseAsync(translatedEvent with { Headers = headers }, cancellationToken);
    }
}
