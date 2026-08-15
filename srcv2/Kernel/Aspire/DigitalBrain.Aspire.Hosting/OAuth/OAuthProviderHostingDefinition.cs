namespace DigitalBrain.Aspire.Hosting;

public sealed record OAuthProviderHostingDefinition(
    string Key,
    string DisplayName,
    string ParameterPrefix,
    string ConfigurationRoot,
    string ClientIdDescription,
    string? ClientSecretDescription,
    string RedirectUriDescription);