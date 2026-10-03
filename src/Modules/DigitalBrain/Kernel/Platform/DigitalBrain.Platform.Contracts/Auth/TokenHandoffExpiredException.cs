namespace DigitalBrain.Platform.Contracts.Auth;

public sealed class TokenHandoffExpiredException() : InvalidOperationException("The login expired. Sign in again.");
