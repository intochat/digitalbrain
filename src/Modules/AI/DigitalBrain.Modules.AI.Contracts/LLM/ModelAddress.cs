using DigitalBrain.AI.Scripted;

namespace DigitalBrain.AI;

// Turns a model name used in configuration into its neuron: an LLM marker name such as "IGpt56Luna",
// or "scripted/<key>" for a scripted stand-in.
public static class ModelAddress
{
    public static bool IsKnown(string model) => IsScripted(model) || LLMModel.FindByMarkerName(model) is not null;

    public static ILLM Resolve(IGrainFactory grains, string model)
    {
        ArgumentNullException.ThrowIfNull(grains);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        if (IsScripted(model)) { return grains.GetGrain<IScriptedLLM>(model[IScriptedLLM.ModelPrefix.Length..]); }
        var marker = LLMModel.FindByMarkerName(model)?.Marker ?? throw new ArgumentException($"Unknown model '{model}'.", nameof(model));
        return (ILLM)grains.GetGrain(marker, "default");
    }

    public static async Task<string> Complete(IGrainFactory grains, string model, string system, string user, CancellationToken cancellationToken = default)
    {
        var result = await Resolve(grains, model).Generate(new InferenceRequest(
        [
            new AiMessage("system", [new AiText(system)]),
            new AiMessage("user", [new AiText(user)]),
        ]), cancellationToken);
        return string.Concat(result.Messages.SelectMany(message => message.Content).OfType<AiText>().Select(content => content.Text)).Trim();
    }

    private static bool IsScripted(string model) => model.StartsWith(IScriptedLLM.ModelPrefix, StringComparison.Ordinal);
}
