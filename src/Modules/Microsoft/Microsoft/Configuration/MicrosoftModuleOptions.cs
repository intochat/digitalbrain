namespace DigitalBrain.Microsoft;

/// <summary>Public, non-secret module settings. Credentials stay in the host's secret configuration.</summary>
public sealed record MicrosoftModuleOptions
{
    public string? AspireProjectPath { get; init; }

    public string AspireApplicationName { get; init; } = "DigitalBrain";
}