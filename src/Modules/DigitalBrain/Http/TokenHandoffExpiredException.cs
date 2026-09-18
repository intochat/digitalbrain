namespace DigitalBrain.Http;

public sealed class TokenHandoffExpiredException() : InvalidOperationException("The login expired. Sign in again.");
