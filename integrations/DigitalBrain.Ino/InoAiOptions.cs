using Microsoft.Extensions.AI;

namespace DigitalBrain.Ino;

// AI configuration owned by the Ino (personal AI assistant) integration.
// Ships common assistant AI logic/settings (provider, prompts, etc.) so Ino
// can be treated as a pluggable integration like Google/Salesforce.
public sealed class InoAiOptions
{
    // Preferred provider for Ino assistant calls (ollama | openai | azure ...)
    public string Provider { get; set; } = "ollama";

    // Optional model override specific to Ino reasoning / intent.
    public string? Model { get; set; }

    // System prompt baseline for Ino LLM calls (classification + response planning).
    public string SystemPrompt { get; set; } = "You are INO, an ultra-context personal AI assistant. Be concise, actionable, and remember context from previous interactions.";

    // Temperature for Ino responses.
    public float Temperature { get; set; } = 0.2f;
}
