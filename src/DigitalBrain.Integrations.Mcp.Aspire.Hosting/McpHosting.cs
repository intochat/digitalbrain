using System.Runtime.CompilerServices;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Aspire.Hosting;

namespace DigitalBrain.Integrations.Mcp.Aspire.Hosting;

internal sealed record McpProviderHostingDefinition(
    string Key,
    string DisplayName,
    string ParameterPrefix,
    string ConfigurationRoot,
    string ClientIdDescription,
    string ClientSecretDescription,
    string RedirectUriDescription);

internal static class McpProviderHosting
{
    private static readonly ConditionalWeakTable<IDistributedApplicationBuilder, McpApplicationParameters>
        Applications = new();
    private static readonly ConditionalWeakTable<BrainService, McpBrainReference> Brains = new();

    internal static void Register(BrainService brain, McpProviderHostingDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(brain);
        ArgumentNullException.ThrowIfNull(definition);

        var application = Applications.GetValue(
            brain.Builder,
            static builder => new McpApplicationParameters(builder));
        var reference = Brains.GetValue(brain, CreateBrainReference);
        reference.Add(application.Register(definition));
    }

    private static McpBrainReference CreateBrainReference(BrainService brain)
    {
        var application = Applications.GetValue(
            brain.Builder,
            static builder => new McpApplicationParameters(builder));
        var reference = new McpBrainReference(brain, application.AuthorizationMode);

        BrainModuleHosting.RequireStateProtection(brain);
        BrainModuleHosting.AddReference(brain, reference);
        return reference;
    }

    private sealed class McpApplicationParameters(IDistributedApplicationBuilder builder)
    {
        private readonly Dictionary<string, McpProviderParameters> _providers = new(StringComparer.Ordinal);

        internal IResourceBuilder<ParameterResource> AuthorizationMode { get; } = builder
            .AddParameter("mcp-authorization-mode")
            .WithDescription(
                "MCP authorization execution mode. Use `LocalLoopbackDevelopment` only for a local silo; production hosts must register an authenticated-edge authorization adapter.",
                enableMarkdown: true);

        internal McpProviderParameters Register(McpProviderHostingDefinition definition)
        {
            Validate(definition);

            if (_providers.TryGetValue(definition.Key, out var existing))
            {
                if (existing.Definition != definition)
                {
                    throw new InvalidOperationException(
                        $"MCP provider key '{definition.Key}' has conflicting AppHost definitions.");
                }

                return existing;
            }

            var created = new McpProviderParameters(
                definition,
                builder.AddParameter($"{definition.ParameterPrefix}-client-id")
                    .WithDescription(definition.ClientIdDescription, enableMarkdown: true),
                builder.AddParameter($"{definition.ParameterPrefix}-client-secret", secret: true)
                    .WithDescription(definition.ClientSecretDescription, enableMarkdown: true),
                builder.AddParameter($"{definition.ParameterPrefix}-redirect-uri")
                    .WithDescription(definition.RedirectUriDescription, enableMarkdown: true));
            _providers.Add(definition.Key, created);
            return created;
        }

        private static void Validate(McpProviderHostingDefinition definition)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(definition.Key);
            ArgumentException.ThrowIfNullOrWhiteSpace(definition.DisplayName);
            ArgumentException.ThrowIfNullOrWhiteSpace(definition.ParameterPrefix);
            ArgumentException.ThrowIfNullOrWhiteSpace(definition.ConfigurationRoot);
            ArgumentException.ThrowIfNullOrWhiteSpace(definition.ClientIdDescription);
            ArgumentException.ThrowIfNullOrWhiteSpace(definition.ClientSecretDescription);
            ArgumentException.ThrowIfNullOrWhiteSpace(definition.RedirectUriDescription);
        }
    }

    private sealed record McpProviderParameters(
        McpProviderHostingDefinition Definition,
        IResourceBuilder<ParameterResource> ClientId,
        IResourceBuilder<ParameterResource> ClientSecret,
        IResourceBuilder<ParameterResource> RedirectUri);

    private sealed class McpBrainReference(
        BrainService brain,
        IResourceBuilder<ParameterResource> authorizationMode) : BrainModuleReference
    {
        private readonly List<McpProviderParameters> _providers = [];

        internal void Add(McpProviderParameters provider)
        {
            if (_providers.Any(existing => string.Equals(
                existing.Definition.Key,
                provider.Definition.Key,
                StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    $"{provider.Definition.DisplayName} is already configured on brain '{brain.Name}'. Add it exactly once.");
            }

            _providers.Add(provider);
        }

        public override void Apply<T>(IResourceBuilder<T> builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            builder.WithEnvironment(
                "DigitalBrain__Integrations__Mcp__AuthorizationMode",
                authorizationMode);

            foreach (var provider in _providers)
            {
                var root = provider.Definition.ConfigurationRoot.Replace(":", "__", StringComparison.Ordinal);
                builder
                    .WithEnvironment($"{root}__ClientId", provider.ClientId)
                    .WithEnvironment($"{root}__ClientSecret", provider.ClientSecret)
                    .WithEnvironment($"{root}__RedirectUri", provider.RedirectUri);
            }
        }
    }
}
