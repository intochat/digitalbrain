using System.ClientModel;
using DigitalBrain.SDK.DigitalBrain.Ai.Llm;
using DigitalBrain.SDK.DigitalBrain.Ai.Models;
using Microsoft.Extensions.AI;
using OpenAI;

namespace DigitalBrain.SDK.DigitalBrain.Ai.Llm.Providers;

public sealed class OpenAiProviderFactory : ILlmProviderFactory
{
    public string ProviderName => "openai";

    public bool IsConfigured(IConfiguration config)
        => !string.IsNullOrEmpty(config["DigitalBrain:Ai:OpenAiApiKey"]) ||
           !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OPENAI_API_KEY"));

    public IChatClient CreateClient(LlmModel model, IConfiguration config)
    {
        var apiKey = config["DigitalBrain:Ai:OpenAiApiKey"];
        
        if (string.IsNullOrEmpty(apiKey) || apiKey == "placeholder")
        {
            apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
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
                        var decrypted = secretVault.DecryptSecret("openai-api-key");
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
                $"OpenAI API key is required for provider 'openai' (model '{model.Id}'), but is not set. " +
                "Please configure it securely by entering standard command: 'set-private global:openai-api-key=<your-key>' in the visual client prompt.");

        var clientOptions = new OpenAIClientOptions();
        var openAi = new OpenAIClient(new ApiKeyCredential(apiKey), clientOptions);
        return openAi.GetChatClient(model.Id).AsIChatClient();
    }
}
