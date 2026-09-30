using Microsoft.Extensions.Configuration;
using DigitalBrain.AI;
using Microsoft.Extensions.Options;

namespace DigitalBrain.Assistant;

// This is the browser's allowlist. Never serialize a resolved model: it contains
// operator-owned endpoints. Provider credentials remain entirely server-owned.
public sealed class AgentModelCatalog(ModelProfiles profiles, IOptionsMonitor<AIOptions> options, IConfiguration configuration)
{
    public ModelCatalog Read()
    {
        var automatic = Resolve(Default());
        return new(Describe(null, "Automatic (workspace default)", automatic),
            Choices().Select(choice => (choice, resolved: Resolve(choice.Selection)))
                .Where(item => item.resolved is not null)
                .Select(item => Describe(item.choice.Id, item.choice.Label, item.resolved)).ToArray());
    }

    public AgentModelSelection? Select(string? id)
    {
        var selection = id is null ? Default()
            : Choices().FirstOrDefault(choice => string.Equals(choice.Id, id, StringComparison.Ordinal))?.Selection
                ?? throw new ArgumentException("The selected model is unavailable. Choose a model from Settings.");
        if (Resolve(selection) is null)
        {
            throw new ArgumentException("The selected model is unavailable or does not support assistant tools. Choose a model from Settings.");
        }
        return selection;
    }

    private AgentModelSelection? Default() => configuration["IntoChat:Assistant:Model"] is { Length: > 0 } model
        ? new(Model: model) : null;

    private IEnumerable<Choice> Choices()
    {
        foreach (var name in options.CurrentValue.ModelProfiles.Keys.Order(StringComparer.OrdinalIgnoreCase))
        {
            yield return new("profile:" + name, name, new(Profile: name));
        }
        foreach (var preset in LLMModel.All)
        {
            // A local server being configured does not imply every compiled local model is installed.
            if (preset.Provider == AiProvider.Ollama && !options.CurrentValue.Ollama.Models.ContainsKey(preset.Marker.Name)) { continue; }
            yield return new("preset:" + preset.Marker.Name, preset.Id, new(Model: preset.Marker.Name));
        }
    }

    private ResolvedAgentModel? Resolve(AgentModelSelection? selection)
    {
        try { return profiles.Resolve(selection, requiresTools: true); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or NotSupportedException)
        { return null; }
    }

    private static ModelChoice Describe(string? id, string label, ResolvedAgentModel? model) => new(
        id, label, model?.Provider, model?.Model, model is not null,
        model is null ? [] : Enum.GetValues<LlmCapabilities>()
            .Where(capability => capability != LlmCapabilities.None && model.Capabilities.HasFlag(capability))
            .Select(capability => capability.ToString()).ToArray());

    private sealed record Choice(string Id, string Label, AgentModelSelection Selection);
    [GenerateSerializer, Alias("assistant.model-catalog")]
    public sealed record ModelCatalog([property: Id(0)] ModelChoice Automatic, [property: Id(1)] IReadOnlyList<ModelChoice> Models);
    [GenerateSerializer, Alias("assistant.model-choice")]
    public sealed record ModelChoice([property: Id(0)] string? Id, [property: Id(1)] string Label, [property: Id(2)] string? Provider, [property: Id(3)] string? Model, [property: Id(4)] bool Available, [property: Id(5)] IReadOnlyList<string> Capabilities);
}
