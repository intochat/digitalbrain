using DigitalBrain.Runtime.Dynamic;

namespace DigitalBrain.SDK.DigitalBrain.Ai.Planning;

public static class PlannerPrompt
{
    public static string BuildUserPrompt(PlanNeuronRequest req)
    {
        var prompt = req.LastError is null
            ? req.Intent
            : $"{req.Intent} ATTEMPT {req.Attempt} last error: {req.LastError}";

        if (req.PinnedLlmModel is { Length: > 0 } pinned)
            prompt +=
                $"\n\nThis neuron MUST inject its chat client as "
                + $"[Llm<{pinned}>] IChatClient; do not choose another model.";

        return prompt;
    }
}
