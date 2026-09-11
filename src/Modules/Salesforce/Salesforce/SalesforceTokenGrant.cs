namespace DigitalBrain.Salesforce;

internal sealed record SalesforceTokenGrant(string AccessToken, string? RefreshToken, double? ExpiresInSeconds);
