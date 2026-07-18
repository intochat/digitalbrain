using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.DigitalBrain.SoftwareEngineering.Developer;

/// <summary>
/// Synapse requesting GitHub browser authentication or raw token setup.
/// </summary>
[GenerateSerializer]
public sealed record GitHubAuthRequest([property: Id(1)] string? PersonalAccessToken = null, // Raw token if input directly
    [property: Id(2)] string? CustomScope = null         // Custom scope requested
) : Synapse;

/// <summary>
/// Synapse returning GitHub auth details, containing either a verification/browser login URL or a success confirmation.
/// </summary>
[GenerateSerializer]
public sealed record GitHubAuthResponse([property: Id(1)] bool Authenticated,
    [property: Id(2)] string? VerificationUrl = null,      // URL to open in a browser for OAuth flow
    [property: Id(3)] string? UserCode = null,             // Device flow user verification code
    [property: Id(4)] string? ErrorMessage = null
) : Synapse;
