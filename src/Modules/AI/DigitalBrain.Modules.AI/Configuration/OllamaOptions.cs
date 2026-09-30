using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.AI;

public sealed class OllamaOptions : AIProviderOptions
{
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public Dictionary<string, AIModelOptions> Models { get; } = new(StringComparer.OrdinalIgnoreCase);
}
