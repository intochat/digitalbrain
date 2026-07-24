using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Abstractions;
using DigitalBrain.Aspire.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Integrations.Mcp.Aspire.Hosting;

internal sealed record McpProviderHostingDefinition(
    string Key,
    string DisplayName,
    string ParameterPrefix,
    string ConfigurationRoot,
    string ClientIdDescription,
    string? ClientSecretDescription,
    string RedirectUriDescription);

internal static class McpProviderHosting
{
    internal static void Register<TModule>(
        DigitalBrainModuleBuilder<TModule> module,
        McpProviderHostingDefinition definition)
        where TModule : class, IModule, new()
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(definition);

        var application = GetOrAddApplicationParameters(module.Brain.GetApplicationBuilder());
        var projection = module.Brain.GetOrAddState(
            brain => new McpBrainProjection(brain, application.AuthorizationMode),
            out var added);
        if (added)
        {
            module.RequireStateProtection();
            module.AddProjection(projection);
        }

        projection.Add(definition, application.Register(definition));
    }

    private static McpApplicationParameters GetOrAddApplicationParameters(
        IDistributedApplicationBuilder builder)
    {
        var existing = builder.Services
            .LastOrDefault(descriptor => descriptor.ServiceType == typeof(McpApplicationParameters))
            ?.ImplementationInstance as McpApplicationParameters;
        if (existing is not null)
        {
            return existing;
        }

        var created = new McpApplicationParameters(builder);
        builder.Services.AddSingleton(created);
        return created;
    }

    private sealed class McpApplicationParameters
    {
        private readonly IDistributedApplicationBuilder _builder;
        private readonly Dictionary<string, McpProviderParameters> _providers =
            new(StringComparer.Ordinal);

        internal McpApplicationParameters(IDistributedApplicationBuilder builder)
        {
            _builder = builder;
            AuthorizationMode = (_builder.ExecutionContext.IsRunMode
                    ? _builder.AddParameter("mcp-authorization-mode", "LocalLoopbackDevelopment")
                    : _builder.AddParameter("mcp-authorization-mode"))
                .WithDescription(
                    "MCP authorization execution mode. Use `LocalLoopbackDevelopment` only for a local silo; every other value disables interactive authorization.",
                    enableMarkdown: true);
        }

        internal IResourceBuilder<ParameterResource> AuthorizationMode { get; }

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

            var localRun = _builder.ExecutionContext.IsRunMode;
            var created = new McpProviderParameters(
                definition,
                Parameter(
                    $"{definition.ParameterPrefix}-client-id",
                    localValue: "local-dev",
                    secret: false,
                    description: definition.ClientIdDescription,
                    localRun),
                definition.ClientSecretDescription is { } clientSecretDescription
                    ? Parameter(
                        $"{definition.ParameterPrefix}-client-secret",
                        localValue: "local-dev-secret",
                        secret: true,
                        description: clientSecretDescription,
                        localRun)
                    : null,
                Parameter(
                    $"{definition.ParameterPrefix}-redirect-uri",
                    localValue: "http://localhost/oauth/callback",
                    secret: false,
                    description: definition.RedirectUriDescription,
                    localRun));
            _providers.Add(definition.Key, created);
            return created;
        }

        private IResourceBuilder<ParameterResource> Parameter(
            string name,
            string localValue,
            bool secret,
            string description,
            bool localRun)
        {
            var resource = localRun
                ? _builder.AddParameter(name, localValue, secret: secret)
                : _builder.AddParameter(name, secret: secret);

            return resource.WithDescription(description, enableMarkdown: true);
        }
    }

    private sealed class McpBrainProjection : DigitalBrainModuleProjection
    {
        private readonly DigitalBrainBuilder _brain;
        private readonly Dictionary<string, McpProviderParameters> _providers =
            new(StringComparer.Ordinal);
        private readonly IResourceBuilder<ParameterResource> _authorizationMode;

        internal McpBrainProjection(
            DigitalBrainBuilder brain,
            IResourceBuilder<ParameterResource> authorizationMode)
        {
            _brain = brain;
            _authorizationMode = authorizationMode;
        }

        internal void Add(
            McpProviderHostingDefinition definition,
            McpProviderParameters parameters)
        {
            if (_providers.ContainsKey(definition.Key))
            {
                throw new InvalidOperationException(
                    $"{definition.DisplayName} is already configured on brain '{_brain.Name}'. Add it exactly once.");
            }

            _providers.Add(definition.Key, parameters);
        }

        public override void Apply<TResource>(IResourceBuilder<TResource> builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            builder.WithEnvironment(
                "DigitalBrain__Integrations__Mcp__AuthorizationMode",
                _authorizationMode);

            foreach (var provider in _providers.Values)
            {
                var root = provider.Definition.ConfigurationRoot.Replace(
                    ":",
                    "__",
                    StringComparison.Ordinal);
                builder
                    .WithEnvironment($"{root}__ClientId", provider.ClientId)
                    .WithEnvironment($"{root}__RedirectUri", provider.RedirectUri);

                if (provider.ClientSecret is not null)
                {
                    builder.WithEnvironment($"{root}__ClientSecret", provider.ClientSecret);
                }
            }
        }
    }

    private static void Validate(McpProviderHostingDefinition definition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.Key);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.DisplayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.ParameterPrefix);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.ConfigurationRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.ClientIdDescription);
        if (definition.ClientSecretDescription is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(definition.ClientSecretDescription);
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(definition.RedirectUriDescription);
    }

    private sealed record McpProviderParameters(
        McpProviderHostingDefinition Definition,
        IResourceBuilder<ParameterResource> ClientId,
        IResourceBuilder<ParameterResource>? ClientSecret,
        IResourceBuilder<ParameterResource> RedirectUri);
}
