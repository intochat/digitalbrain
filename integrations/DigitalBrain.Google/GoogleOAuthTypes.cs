using Orleans;

namespace DigitalBrain.Google;

[GenerateSerializer]
[Alias("DigitalBrain.Google.GoogleOAuthCallback")]
public record GoogleOAuthCallback(
    string? Code,
    string? State,
    string? Error,
    string? ErrorDescription,
    string FallbackRedirectUri);

[GenerateSerializer]
[Alias("DigitalBrain.Google.GoogleOAuthCallbackResult")]
public record GoogleOAuthCallbackResult(
    bool Success,
    string Title,
    string Message);
