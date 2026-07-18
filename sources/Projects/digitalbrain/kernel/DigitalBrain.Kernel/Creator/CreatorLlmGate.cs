using DigitalBrain.SDK.DigitalBrain.Ai.Models;

namespace DigitalBrain.Kernel.Creator;

public static class CreatorLlmGate
{
    public static bool IsRejected(string featureText, out string message, out string detail)
    {
        if (FeatureLlmTag.TryRead(featureText, out var model)
            && !LlmModel.All.Any(m =>
                string.Equals(m.GetType().Name, model, StringComparison.OrdinalIgnoreCase)))
        {
            var known = string.Join(", ", LlmModel.All.Select(m => m.GetType().Name));
            message = $"llm: unknown model '{model}'";
            detail = $"llm: unknown model '{model}' (known: {known})";
            return true;
        }

        message = string.Empty;
        detail = string.Empty;
        return false;
    }
}
