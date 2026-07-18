using System.Linq;
using Aspire.Hosting;
using DigitalBrain.Runtime.Ai;

namespace DigitalBrain.Hosting.DigitalBrain;

public sealed class AiDomainBuilder(DigitalBrainResource digitalbrain) : DigitalBrainDomainBuilder(digitalbrain)
{
    public AiDomainBuilder WithLlmProvider<TProvider>() where TProvider : ILlmProvider
    {
        if (TProvider.SecretParameterName is { } secretName)
            DigitalBrain.SecretParam(secretName, TProvider.SecretDescription!);
        return this;
    }

    public AiDomainBuilder WithEmbedding<TModel>() where TModel : EmbeddingModel, new()
    {
        var m = new TModel();
        DigitalBrain.EmbeddingModel = new DeclaredEmbeddingModel(
            m.Id, m.Provider, m.DisplayName, m.Icon, m.Dimensions);
        EnsureProviderSecret(m.Provider);
        return this;
    }

    public AiDomainBuilder WithVoice2Text<TModel>() where TModel : IVoiceModel
    {
        DigitalBrain.VoiceModel = new DeclaredVoiceModel(
            TModel.Id, TModel.DisplayName, TModel.Icon, TModel.ModelFileName, TModel.ModelFileSha256);
        return this;
    }

    void EnsureProviderSecret(string provider)
    {
        switch (provider)
        {
            case "openai":
                DigitalBrain.SecretParam(
                    "openai-api-key",
                    "Get your key at [platform.openai.com/api-keys](https://platform.openai.com/api-keys).");
                break;
            case "anthropic":
                DigitalBrain.SecretParam(
                    "anthropic-api-key",
                    "Get your key at [console.anthropic.com/settings/keys](https://console.anthropic.com/settings/keys).");
                break;
            case "grok":
                DigitalBrain.SecretParam(
                    "grok-api-key",
                    "Get your key at [console.x.ai](https://console.x.ai/).");
                break;
        }
    }

    internal override void ApplyTo(IResourceBuilder<ProjectResource> silo)
    {
        var useMock = Environment.GetEnvironmentVariable("DigitalBrain__Ai__UseMockClient") ?? "false";
        silo.WithEnvironment("DigitalBrain__Ai__UseMockClient", useMock);
        silo.WithEnvironment("DigitalBrain__Ai__PrivateCluster", "true");
        silo.WithEnvironment("DigitalBrain__Ai__LocalModel", "nemotron-mini");

        var ollama = DigitalBrain.AppBuilder.Resources.OfType<IResourceWithEndpoints>()
            .FirstOrDefault(r => string.Equals(r.Name, "ino-llm", StringComparison.OrdinalIgnoreCase));
        if (ollama is not null)
        {
            silo.WithEnvironment("services__ollama__http__0", ollama.GetEndpoint("http"));
        }

        if (DigitalBrain.EmbeddingModel is { } embedding)
        {
            silo.WithEnvironment("DigitalBrain__Ai__Embedding__Id", embedding.Id);
            silo.WithEnvironment("DigitalBrain__Ai__Embedding__Provider", embedding.Provider);
            silo.WithEnvironment(
                "DigitalBrain__Ai__Embedding__Dimensions",
                embedding.Dimensions.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (DigitalBrain.VoiceModel is { } voice)
        {
            silo.WithEnvironment("DigitalBrain__Ai__Voice__Id", voice.Id);
            silo.WithEnvironment("DigitalBrain__Ai__Voice__FileName", voice.ModelFileName);
            if (voice.ModelFileSha256 is { } sha)
                silo.WithEnvironment("DigitalBrain__Ai__Voice__Sha256", sha);
        }

        if (DigitalBrain.Secrets.TryGetValue("openai-api-key", out var openAi))
            silo.WithEnvironment("DigitalBrain__Ai__OpenAiApiKey", openAi);
        if (DigitalBrain.Secrets.TryGetValue("anthropic-api-key", out var anthropic))
            silo.WithEnvironment("DigitalBrain__Ai__AnthropicApiKey", anthropic);
        if (DigitalBrain.Secrets.TryGetValue("grok-api-key", out var grok))
            silo.WithEnvironment("DigitalBrain__Ai__GrokApiKey", grok);
    }
}
