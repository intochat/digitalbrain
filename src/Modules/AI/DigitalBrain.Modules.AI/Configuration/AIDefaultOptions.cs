using Microsoft.Extensions.Configuration;

namespace DigitalBrain.AI;

public sealed class AIDefaultOptions
{
    public string? Profile { get; set; }
    public string? Provider { get; set; }
    public string? Model { get; set; }
    public string? Reasoning { get; set; }
    public int? MaxOutputTokens { get; set; }
    public LlmCapabilities? Capabilities { get; set; }
    public string? Embedding { get; set; }
    public string? Transcription { get; set; }
    public string? Image { get; set; }
}
