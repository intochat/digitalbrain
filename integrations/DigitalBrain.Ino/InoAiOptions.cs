using Microsoft.Extensions.AI;

namespace DigitalBrain.Ino;

public sealed class InoAiOptions
{
    public string Provider { get; set; } = "ollama";

    public string? Model { get; set; }

    public string SystemPrompt { get; set; } = "You are INO, an ultra-context personal AI assistant. Be concise, actionable, and remember context from previous interactions.";

    public float Temperature { get; set; } = 0.2f;
}
