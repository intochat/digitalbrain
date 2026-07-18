using DigitalBrain.SDK.DigitalBrain.Ai.Llm;
using DigitalBrain.SDK.DigitalBrain.Ai.Models;
using DigitalBrain.SDK.XAI.Grok;
using Microsoft.Extensions.AI;

namespace DigitalBrain.SDK.DigitalBrain.Ai.Llm.Providers;

public sealed class GrokProviderFactory : ILlmProviderFactory
{
    public string ProviderName => "grok";

    public bool IsConfigured(IConfiguration config)
        => !string.IsNullOrEmpty(config["DigitalBrain:Ai:GrokApiKey"]) ||
           !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("XAI_API_KEY"));

    public IChatClient CreateClient(LlmModel model, IConfiguration config)
    {
        var apiKey = config["DigitalBrain:Ai:GrokApiKey"];
        
        if (string.IsNullOrEmpty(apiKey) || apiKey == "placeholder")
        {
            apiKey = Environment.GetEnvironmentVariable("XAI_API_KEY");
        }

        if (string.IsNullOrEmpty(apiKey) || apiKey == "placeholder")
        {
            if (SdkRuntime.ServiceProvider is { } sp)
            {
                var secretVault = sp.GetService(typeof(Security.ISecretVault)) as Security.ISecretVault;
                if (secretVault is not null)
                {
                    try
                    {
                        var decrypted = secretVault.DecryptSecret("grok-api-key");
                        if (!string.IsNullOrEmpty(decrypted) && decrypted != "placeholder")
                        {
                            apiKey = decrypted;
                        }
                    }
                    catch (KeyNotFoundException) { }
                    catch (Exception) { }
                }
            }
        }

        if (string.IsNullOrEmpty(apiKey) || apiKey == "placeholder")
            throw new InvalidOperationException(
                $"Grok API key is required for provider 'grok' (model '{model.Id}'), but is not set. " +
                "Please configure it securely by entering standard command: 'set-private global:grok-api-key=<your-key>' in the visual client prompt.");

        return new GrokConnector(apiKey, model.Id);
    }
}
