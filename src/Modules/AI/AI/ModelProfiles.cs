using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace DigitalBrain.AI;

public sealed class ModelProfiles(IServiceProvider services, IOptionsMonitor<AIOptions> options)
{
    public ResolvedAgentModel Resolve(AgentModelSelection? selection, bool requiresTools = false)
    {
        selection ??= new();
        var configuration = options.CurrentValue;
        var useDefault = string.IsNullOrWhiteSpace(selection.Profile)
            && string.IsNullOrWhiteSpace(selection.Provider) && string.IsNullOrWhiteSpace(selection.Model);
        var profileName = Clean(selection.Profile) ?? (useDefault ? Clean(configuration.Default.Profile) : null);
        var providerName = Clean(selection.Provider) ?? (useDefault ? Clean(configuration.Default.Provider) : null);
        var modelName = Clean(selection.Model) ?? (useDefault ? Clean(configuration.Default.Model) : null);
        var reasoning = Clean(selection.Reasoning) ?? (useDefault ? Clean(configuration.Default.Reasoning) : null);
        var maxTokens = selection.MaxOutputTokens ?? (useDefault ? configuration.Default.MaxOutputTokens : null);
        var declared = selection.Capabilities ?? (useDefault ? configuration.Default.Capabilities : null);
        string? endpoint = null;

        if (profileName is not null)
        {
            if (!useDefault && (providerName is not null || modelName is not null))
            {
                throw new ArgumentException("Select either a named model profile or an explicit provider/model, not both.", nameof(selection));
            }
            if (!configuration.ModelProfiles.TryGetValue(profileName, out var profile))
            {
                throw new ArgumentException($"Model profile '{profileName}' is not configured in {AIOptions.SectionName}:ModelProfiles.", nameof(selection));
            }
            providerName = Clean(profile.Provider);
            modelName = Clean(profile.Model);
            reasoning = Clean(selection.Reasoning) ?? Clean(profile.Reasoning);
            maxTokens = selection.MaxOutputTokens ?? profile.MaxOutputTokens;
            declared = selection.Capabilities ?? profile.Capabilities;
            endpoint = Clean(profile.Endpoint);
            if (modelName is null) { throw new ArgumentException($"Model profile '{profileName}' requires a model ID.", nameof(selection)); }
        }

        var preset = modelName is null ? null : LLMModel.FindByMarkerName(modelName) ?? LLMModel.FindById(modelName);
        AiProvider provider;
        if (providerName is not null)
        {
            provider = ParseProvider(providerName);
            if (preset is not null && preset.Provider != provider)
            {
                throw new ArgumentException($"Model '{modelName}' belongs to {preset.Provider}, not {provider}. Select a model served by the requested provider.", nameof(selection));
            }
        }
        else if (preset is not null) { provider = preset.Provider; }
        else if (modelName is not null)
        {
            throw new ArgumentException($"Model '{modelName}' is not a compiled preset. Specify its provider explicitly or configure a named model profile.", nameof(selection));
        }
        else
        {
            preset = LLMModel.All.FirstOrDefault(model => AIClients.Factory(model.Provider).IsConfigured(configuration))
                ?? throw new InvalidOperationException("No chat provider is configured. Configure a provider API key or an Ollama endpoint.");
            provider = preset.Provider;
        }

        if (modelName is null)
        {
            preset ??= LLMModel.All.FirstOrDefault(model => model.Provider == provider)
                ?? throw new ArgumentException($"Provider '{provider}' requires an explicit model ID.", nameof(selection));
        }
        var modelId = preset?.Id ?? modelName!;
        if (provider == AiProvider.Ollama && preset is not null
            && (modelName is null || string.Equals(modelName, preset.Marker.Name, StringComparison.Ordinal)))
        {
            modelId = Clean(configuration.Ollama.Models.GetValueOrDefault(preset.Marker.Name)?.Model) ?? modelId;
        }
        if (modelId.Length > 200 || modelId.Any(char.IsControl))
        {
            throw new ArgumentException("Model ID must contain at most 200 printable characters.", nameof(selection));
        }
        var factory = AIClients.Factory(provider);
        if (!factory.IsConfigured(configuration) && !(provider == AiProvider.Ollama && endpoint is not null))
        {
            throw new InvalidOperationException($"Provider '{provider}' is not configured. Set {AIOptions.SectionName}:{provider}:"
                + (provider == AiProvider.Ollama ? "Endpoint." : "ApiKey."));
        }
        endpoint = Endpoint(endpoint ?? Clean(configuration.Provider(provider).Endpoint), provider);
        var capabilities = declared ?? preset?.Capabilities ?? LlmCapabilities.None;
        if ((capabilities & ~(LlmCapabilities.Tools | LlmCapabilities.Vision | LlmCapabilities.StructuredOutput)) != 0)
        {
            throw new ArgumentException("Model capabilities contain unknown flags.", nameof(selection));
        }
        if (requiresTools && !capabilities.HasFlag(LlmCapabilities.Tools))
        {
            throw new ArgumentException($"Model '{modelId}' does not declare tool support. Configure Tools capabilities in its model profile or selection before attaching tools.", nameof(selection));
        }
        reasoning = NormalizeReasoning(reasoning);
        if (requiresTools && provider == AiProvider.OpenAI && modelId.StartsWith("gpt-5.6", StringComparison.OrdinalIgnoreCase))
        {
            if (reasoning is not null and not "none")
            {
                throw new ArgumentException("GPT-5.6 tools through OpenAI Chat Completions require reasoning 'none'.", nameof(selection));
            }
            reasoning = "none";
        }
        if (maxTokens is < 1 or > 1_000_000)
        {
            throw new ArgumentException("MaxOutputTokens must be between 1 and 1000000.", nameof(selection));
        }
        var resolved = new ResolvedAgentModel(provider.ToString(), modelId, endpoint, profileName, string.Empty,
            capabilities, reasoning, maxTokens);
        return resolved with { Revision = Revision(resolved) };
    }

    // Callers own these pipelines. Recreating a client never rereads a changed profile or default.
    public IChatClient CreateClient(ResolvedAgentModel resolved)
        => AIClients.BuildChatPipeline(services, resolved.Capabilities.HasFlag(LlmCapabilities.Tools),
            resolved.Provider, CreateInferenceClient(resolved), rejectUnsupportedTools: true);

    // Inference returns proposed calls. The agent owns the only tool invocation loop.
    public IChatClient CreateInferenceClient(ResolvedAgentModel resolved)
    {
        ArgumentNullException.ThrowIfNull(resolved);
        if (!string.Equals(resolved.Revision, Revision(resolved), StringComparison.Ordinal))
        {
            throw new ArgumentException("The resolved model configuration does not match its revision.", nameof(resolved));
        }
        var provider = ParseProvider(resolved.Provider);
        var configuration = new AIOptions();
        configuration.Provider(provider).ApiKey = options.CurrentValue.Provider(provider).ApiKey;
        configuration.Provider(provider).Endpoint = resolved.Endpoint;
        var inner = AIClients.Factory(provider).CreateChatClient(resolved.Model, configuration);
        var pinned = new ChatClientBuilder(inner).ConfigureOptions(request =>
        {
            request.ModelId = resolved.Model;
            if (resolved.Reasoning is not null) { request.Reasoning = Reasoning(resolved.Reasoning); }
            if (resolved.MaxOutputTokens is not null) { request.MaxOutputTokens = resolved.MaxOutputTokens; }
        }).Build();
        return pinned;
    }

    public static ChatOptions CreateOptions(ResolvedAgentModel resolved)
    {
        ArgumentNullException.ThrowIfNull(resolved);
        return new ChatOptions
        {
            ModelId = resolved.Model,
            Reasoning = resolved.Reasoning is null ? null : Reasoning(resolved.Reasoning),
            MaxOutputTokens = resolved.MaxOutputTokens,
        };
    }

    public IReadOnlyList<ResolvedAgentModel> List()
    {
        var configuration = options.CurrentValue;
        var result = new List<ResolvedAgentModel>();
        foreach (var preset in LLMModel.All.Where(model => AIClients.Factory(model.Provider).IsConfigured(configuration)))
        {
            result.Add(Resolve(new(Model: preset.Marker.Name)));
        }
        foreach (var profile in configuration.ModelProfiles.Keys.Order(StringComparer.OrdinalIgnoreCase))
        {
            result.Add(Resolve(new(Profile: profile)));
        }
        return result.AsReadOnly();
    }

    private static string Revision(ResolvedAgentModel resolved)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(resolved with { Revision = string.Empty }))));

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static AiProvider ParseProvider(string name)
        => Enum.TryParse<AiProvider>(name, ignoreCase: true, out var provider) && Enum.IsDefined(provider)
            && provider is not AiProvider.FoundryLocal
            ? provider : throw new ArgumentException($"Unknown runtime chat provider '{name}'. Use OpenAI, Anthropic, Google, XAI or Ollama.", nameof(name));

    private static string? Endpoint(string? endpoint, AiProvider provider)
    {
        endpoint ??= provider switch
        {
            AiProvider.OpenAI => "https://api.openai.com/v1",
            AiProvider.Anthropic => "https://api.anthropic.com",
            AiProvider.Google => "https://generativelanguage.googleapis.com/v1beta/openai/",
            AiProvider.XAI => "https://api.x.ai/v1",
            _ => null,
        };
        if (endpoint is null || !Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
        {
            throw new ArgumentException("Provider endpoints must be absolute HTTP(S) URLs without credentials, query strings or fragments.", nameof(endpoint));
        }
        return uri.AbsoluteUri;
    }

    private static string? NormalizeReasoning(string? value) => value?.ToLowerInvariant() switch
    {
        null => null,
        "none" => "none",
        "low" => "low",
        "medium" => "medium",
        "high" => "high",
        "xhigh" or "extrahigh" or "extra-high" => "xhigh",
        _ => throw new ArgumentException("Reasoning must be none, low, medium, high or xhigh.", nameof(value)),
    };

    private static ReasoningOptions Reasoning(string value) => new()
    {
        Effort = value switch
        {
            "none" => ReasoningEffort.None,
            "low" => ReasoningEffort.Low,
            "medium" => ReasoningEffort.Medium,
            "high" => ReasoningEffort.High,
            "xhigh" => ReasoningEffort.ExtraHigh,
            _ => throw new ArgumentException("Invalid pinned reasoning effort.", nameof(value)),
        },
    };
}