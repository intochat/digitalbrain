using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Aspire.Hosting;

namespace DigitalBrain.Google.Aspire.Hosting;

public static class GoogleHostingExtensions
{
    public static DigitalBrainModuleBuilder<GoogleModule> WithGmail(
        this DigitalBrainModuleBuilder<GoogleModule> module, Action<GmailHostingOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(configure);
        var options = new GmailHostingOptions();
        configure(options);
        return module.WithGmail(options.PublicOrigin);
    }

    // Only the kernel receives these values. The public origin defaults to the kernel's own
    // http endpoint; pass one explicitly when a reverse proxy fronts the callback.
    public static DigitalBrainModuleBuilder<GoogleModule> WithGmail(
        this DigitalBrainModuleBuilder<GoogleModule> module,
        Uri? publicOrigin = null)
    {
        ArgumentNullException.ThrowIfNull(module);
        var state = module.DigitalBrainBuilder.GetOrAddState(brain => new GmailHostingState(brain, module.Resource), out var added);
        if (added)
        {
            module.AddProjection(state);
        }

        state.Enable(publicOrigin);
        return module;
    }

    private sealed class GmailHostingState(
        DigitalBrainBuilder brain,
        IResourceBuilder<DigitalBrainModuleResource> module) : DigitalBrainModuleProjection
    {
        private const string Root = GoogleModule.GmailOAuthConfigurationRoot;

        private bool _enabled;
        private Uri? _publicOrigin;
        private IResourceBuilder<ParameterResource>? _clientId;
        private IResourceBuilder<ParameterResource>? _clientSecret;

        internal void Enable(Uri? publicOrigin)
        {
            _enabled = true;
            _publicOrigin = publicOrigin ?? _publicOrigin;
        }

        public override void Apply<TResource>(IResourceBuilder<TResource> builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            if (!_enabled)
            {
                return;
            }

            _clientId ??= brain.ApplicationBuilder.AddParameter("gmail-client-id")
                .WithDescription(
                    "OAuth client ID for a Google web client configured for the [Gmail MCP server](https://developers.google.com/workspace/gmail/api/guides/configure-mcp-server). "
                    + "Register http://localhost:5080/integrations/gmail/callback. Only the kernel receives this value.",
                    enableMarkdown: true)
                .WithParentRelationship(module);
            _clientSecret ??= brain.ApplicationBuilder.AddParameter("gmail-client-secret", secret: true)
                .WithDescription(
                    "OAuth client secret for the same Google web client configured for the [Gmail MCP server](https://developers.google.com/workspace/gmail/api/guides/configure-mcp-server). "
                    + "Only the kernel receives this secret; Gmail sign-in happens in your browser when the assistant needs access.",
                    enableMarkdown: true)
                .WithParentRelationship(module);

            builder
                .WithEnvironment(EnvironmentKeys.For(Root, "ClientId"), _clientId)
                .WithEnvironment(EnvironmentKeys.For(Root, "ClientSecret"), _clientSecret);
            if (_publicOrigin is { } origin)
            {
                builder.WithEnvironment(EnvironmentKeys.For(Root, "PublicOrigin"), origin.AbsoluteUri);
            }
            else
            {
                builder.WithEnvironment(EnvironmentKeys.For(Root, "PublicOrigin"), builder.GetEndpoint("http"));
            }
        }
    }
}