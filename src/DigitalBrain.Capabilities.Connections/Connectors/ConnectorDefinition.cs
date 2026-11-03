using System.Reflection;

namespace DigitalBrain.Capabilities.Connections.Connectors;

public sealed record ConnectorDefinition(
    string Id,
    string DisplayName,
    string[] AuthMethods,
    string[] RequiredIntegrations)
{
    public string ExternalSystem { get; init; } = DisplayName;
    public string[] OptionalIntegrations { get; init; } = [];
    public Type[] Neurons { get; init; } = [];
    public Type[] Signals { get; init; } = [];
    public OAuthMethod[] OAuthMethods { get; init; } = [];

    // What a connection offers is its module's ModuleManifest.Provides (05-07, R4-01); a connector declares no capabilities.

    public void Validate()
    {
        var required = RequiredIntegrations ?? [];
        var optional = OptionalIntegrations ?? [];
        foreach (var method in OAuthMethods ?? [])
        {
            if (string.IsNullOrWhiteSpace(method.IntegrationId)
                || (!required.Contains(method.IntegrationId, StringComparer.Ordinal)
                    && !optional.Contains(method.IntegrationId, StringComparer.Ordinal)))
            {
                throw new InvalidOperationException(
                    $"OAuthMethod '{method.Id}' on connector '{Id}' references integration '{method.IntegrationId}', which is not declared in RequiredIntegrations or OptionalIntegrations.");
            }
        }
    }

    private static readonly string[] MemberNames = ["Connector", "Connectors"];

    // A module declares its connectors as a static Connector or Connectors member.
    public static ConnectorDefinition[] DeclaredBy(Type module)
    {
        ArgumentNullException.ThrowIfNull(module);
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly;
        return MemberNames.SelectMany(name =>
            (module.GetProperty(name, flags)?.GetValue(null) ?? module.GetField(name, flags)?.GetValue(null)) switch
            {
                ConnectorDefinition one => [one],
                IEnumerable<ConnectorDefinition> many => many,
                _ => [],
            }).ToArray();
    }
}
