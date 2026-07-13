using DigitalBrain.Core;
using DigitalBrain.Core.Config;
using DigitalBrain.Kernel.Llm;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Kernel;

[GrainType("digitalbrain.llm-responder")]
public class LlmResponderNeuron(
    ILogger<LlmResponderNeuron> logger,
    [Orleans.Runtime.PersistentState("timeline", "Default")]
    Orleans.Runtime.IPersistentState<Runtime.EncryptedRuntimeStateEnvelope> persistentState,
    EncryptedRuntimeStateProtector protector) : Neuron(logger, persistentState, protector), ILlmResponderNeuron
{
    // Cache scoped clients per (provider, key) so a chatty pack does not rebuild a client per message.
    private readonly Dictionary<(string Provider, string? Key), IChatClient> _scopedClients = [];

    public async Task HandleAsync(AskLlm ask, CancellationToken cancellationToken = default)
    {
        var chat = await ResolveChatClientAsync(ask, cancellationToken);
        var text = chat is null ? "[no-llm]" : (await chat.GetResponseAsync(ask.Prompt, cancellationToken: cancellationToken)).Text?.Trim() ?? "[no-llm]";
        var props = new Dictionary<string, object?>(ask.ReplyProps) { ["text"] = text };
        await Broadcast(new Signal(ask.ReplyType, props), cancellationToken);
    }

    private async Task<IChatClient?> ResolveChatClientAsync(AskLlm ask, CancellationToken cancellationToken)
    {
        var factory = ServiceProvider.GetService<IScopedChatClientFactory>();
        var store = ServiceProvider.GetService<IPackConfigStore>();
        var global = ServiceProvider.GetService<IChatClient>();

        // User-controlled global override via "system" pack config (llm_provider / llm_key).
        // This is the first-class persisted selection path (Phase C). Falls back to ask-specific or composition default.
        if (store is not null && factory is not null)
        {
            try
            {
                var sys = await store.GetAsync("system", "llm", cancellationToken);
                if (sys.TryGetValue("llm_provider", out var sysProvider) && !string.IsNullOrWhiteSpace(sysProvider))
                {
                    sys.TryGetValue("llm_key", out var sysKey);
                    var k = (sysProvider, string.IsNullOrEmpty(sysKey) ? null : sysKey);
                    if (!_scopedClients.TryGetValue(k, out var sysClient))
                    {
                        sysClient = factory.Create(sysProvider, k.Item2);
                        if (sysClient is not null)
                        {
                            _scopedClients[k] = sysClient;
                        }
                    }
                    if (sysClient is not null)
                    {
                        return sysClient;
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch { /* config optional */ }
        }

        if (ask.ConfigPack is null)
        {
            return global;
        }

        if (factory is null || store is null)
        {
            return global;
        }

        var values = await store.GetAsync(ask.ConfigScope ?? "default", ask.ConfigPack, cancellationToken);
        if (!values.TryGetValue("llm_provider", out var provider) || string.IsNullOrWhiteSpace(provider))
        {
            return global;
        }

        values.TryGetValue("llm_key", out var apiKey);
        var key = (provider, string.IsNullOrEmpty(apiKey) ? null : apiKey);
        if (!_scopedClients.TryGetValue(key, out var client))
        {
            client = factory.Create(provider, key.Item2);
            if (client is null)
            {
                return global;
            }

            _scopedClients[key] = client;
        }

        return client;
    }
}
