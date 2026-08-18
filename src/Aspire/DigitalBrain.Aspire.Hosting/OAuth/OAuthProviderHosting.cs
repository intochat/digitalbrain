using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Aspire.Hosting;

public static class OAuthProviderHosting
{
    public static void Register<TModule>(DigitalBrainModuleBuilder<TModule> module, OAuthProviderHostingDefinition definition)
        where TModule : class
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(definition);

        var application = GetOrAddApplicationParameters(module.Brain.ApplicationBuilder);
        var projection = module.Brain.GetOrAddState(
            static brain => new OAuthBrainProjection(brain),
            out var added);
        if (added)
        {
            module.RequireStateProtection();
            module.AddProjection(projection);
        }

        projection.Add(definition, application.Register(definition, module.Brain.LocalDevelopmentOAuthCallbackUri));
    }

    private static OAuthApplicationParameters GetOrAddApplicationParameters(IDistributedApplicationBuilder builder)
    {
        var existing = builder.Services
            .LastOrDefault(descriptor => descriptor.ServiceType == typeof(OAuthApplicationParameters))
            ?.ImplementationInstance as OAuthApplicationParameters;
        if (existing is not null)
        {
            return existing;
        }

        var created = new OAuthApplicationParameters(builder);
        builder.Services.AddSingleton(created);
        return created;
    }

    private sealed class OAuthApplicationParameters
    {
        private readonly IDistributedApplicationBuilder _builder;
        private readonly Dictionary<string, OAuthProviderParameters> _providers =
            new(StringComparer.Ordinal);

        internal OAuthApplicationParameters(IDistributedApplicationBuilder builder)
        {
            _builder = builder;
        }

        internal OAuthProviderParameters Register(
            OAuthProviderHostingDefinition definition,
            string? localDevelopmentCallbackUri)
        {
            Validate(definition);

            if (_providers.TryGetValue(definition.Key, out var existing))
            {
                if (existing.Definition != definition)
                {
                    throw new InvalidOperationException(
                        $"OAuth provider key '{definition.Key}' has conflicting AppHost definitions.");
                }

                return existing;
            }

            var localRun = _builder.ExecutionContext.IsRunMode;
            // Prefer the product-composed callback over any persisted stale secret
            // (historically /oauth/mcp/callback). Persist only when the operator must supply it.
            var localCallback = localRun ? localDevelopmentCallbackUri : null;
            var created = new OAuthProviderParameters(
                definition,
                Parameter(
                    $"{definition.ParameterPrefix}-client-id",
                    secret: false,
                    description: definition.ClientIdDescription,
                    localValue: null,
                    persist: true),
                definition.ClientSecretDescription is { } clientSecretDescription
                    ? Parameter(
                        $"{definition.ParameterPrefix}-client-secret",
                        secret: true,
                        description: clientSecretDescription,
                        localValue: null,
                        persist: true)
                    : null,
                Parameter(
                    $"{definition.ParameterPrefix}-redirect-uri",
                    secret: false,
                    description: RedirectUriDescription(definition, localCallback),
                    localCallback,
                    persist: localCallback is null));
            _providers.Add(definition.Key, created);
            return created;
        }

        private static string RedirectUriDescription(
            OAuthProviderHostingDefinition definition,
            string? localCallbackUri)
        {
            var baseText = definition.RedirectUriDescription
                + $" Must end with `{OAuthCallbackPaths.RelativePath}` — that is the path the DigitalBrain kernel serves.";
            return localCallbackUri is null
                ? baseText
                : baseText
                + $" Local `aspire run` defaults this to `{localCallbackUri}` (derived from the product composition, not a persisted secret). "
                + "Register that exact URI on the provider app (Salesforce External Client App / Google OAuth client). "
                + "Override only if you host the callback elsewhere — still ending with "
                + $"`{OAuthCallbackPaths.RelativePath}`.";
        }

        private IResourceBuilder<ParameterResource> Parameter(
            string name,
            bool secret,
            string description,
            string? localValue,
            bool persist)
        {
            var resource = _builder.ExecutionContext.IsRunMode
                ? localValue is null
                    ? _builder.AddParameter(
                        name,
                        new OperatorSuppliedParameterDefault(name),
                        secret: secret,
                        persist: persist)
                    : _builder.AddParameter(
                        name,
                        new ConstantParameterDefault(localValue),
                        secret: secret,
                        persist: persist)
                : _builder.AddParameter(name, secret: secret);

            return resource.WithDescription(description, enableMarkdown: true);
        }
    }

    private sealed class OAuthBrainProjection : DigitalBrainModuleProjection
    {
        private readonly DigitalBrainBuilder _brain;
        private readonly Dictionary<string, OAuthProviderParameters> _providers =
            new(StringComparer.Ordinal);

        internal OAuthBrainProjection(DigitalBrainBuilder brain)
        {
            _brain = brain;
        }

        internal void Add(OAuthProviderHostingDefinition definition, OAuthProviderParameters parameters)
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

            foreach (var provider in _providers.Values)
            {
                var root = provider.Definition.ConfigurationRoot.Replace(":", "__", StringComparison.Ordinal);
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

    private static void Validate(OAuthProviderHostingDefinition definition)
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

    private sealed record OAuthProviderParameters(
        OAuthProviderHostingDefinition Definition,
        IResourceBuilder<ParameterResource> ClientId,
        IResourceBuilder<ParameterResource>? ClientSecret,
        IResourceBuilder<ParameterResource> RedirectUri);
}
