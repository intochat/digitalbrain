using Microsoft.Extensions.Configuration;

namespace DigitalBrain.AI;

public sealed class AIModelProfileOptions
{
    public string? Provider { get; set; }
    public string? Model { get; set; }
    public string? Endpoint { get; set; }
    public string? Reasoning { get; set; }
    public int? MaxOutputTokens { get; set; }
    public LlmCapabilities? Capabilities { get; set; }
    public long? ContextWindowTokens { get; set; }
    public int? MaximumOutputTokens { get; set; }
    public bool? SupportsTemperature { get; set; }
    public bool? SupportsTopP { get; set; }
    public List<string>? AllowedReasoning { get; set; }
}
