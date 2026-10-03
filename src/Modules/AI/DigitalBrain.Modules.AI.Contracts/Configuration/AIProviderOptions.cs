using Microsoft.Extensions.Configuration;

namespace DigitalBrain.AI;

public class AIProviderOptions
{
    // For key-based providers this only seeds the provider registration's Endpoint at startup.
    public string? Endpoint { get; set; }
}
