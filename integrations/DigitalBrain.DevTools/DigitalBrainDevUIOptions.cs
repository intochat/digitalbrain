using Microsoft.AspNetCore.Builder;

namespace DigitalBrain.DevTools;

public sealed class DigitalBrainDevUIOptions
{
    public const string DefaultOwnerConfigurationKey = "DigitalBrain:DevTools:Owner";

    public string OwnerConfigurationKey { get; set; } = DefaultOwnerConfigurationKey;

    public bool AllowProduction { get; set; }

    public bool AllowRemoteAccess { get; set; }

    public string? AuthToken { get; set; }

    public Action<IEndpointConventionBuilder>? ConfigureEndpoints { get; set; }
}

public static class DigitalBrainDevUIAgentNames
{
    public const string Fast = "fast";

    public const string Balanced = "balanced";

    public const string Reasoning = "reasoning";

    public static IReadOnlyList<string> All { get; } =
        [Fast, Balanced, Reasoning];
}
