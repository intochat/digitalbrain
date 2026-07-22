using System.Runtime.CompilerServices;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Aspire.Hosting;

namespace DigitalBrain.Salesforce.Aspire.Hosting;

public static class SalesforceHostingExtensions
{
    private static readonly ConditionalWeakTable<SalesforceModule, SalesforceHostingState> States = new();

    public static SalesforceModule WithSalesforce(this SalesforceModule module)
    {
        ArgumentNullException.ThrowIfNull(module);

        States.GetValue(module, CreateState).AddSalesforce();
        return module;
    }

    private static SalesforceHostingState CreateState(SalesforceModule module)
    {
        var brain = BrainModuleHosting.BrainOf(module);
        var state = new SalesforceHostingState(brain);

        BrainModuleHosting.AddReference(brain, state);
        return state;
    }

    private sealed class SalesforceHostingState(BrainService brain) : BrainModuleReference
    {
        private bool _salesforce;
        private IResourceBuilder<ParameterResource>? _clientId;
        private IResourceBuilder<ParameterResource>? _clientSecret;
        private IResourceBuilder<ParameterResource>? _redirectUri;

        internal void AddSalesforce()
        {
            if (_salesforce)
            {
                throw new InvalidOperationException(
                    $"Salesforce is already configured on brain '{brain.Name}'. Add it exactly once.");
            }

            _salesforce = true;
            _clientId = brain.Builder
                .AddParameter("salesforce-client-id")
                .WithDescription(
                    "Consumer key from the Salesforce [External Client App](https://developer.salesforce.com/docs/platform/hosted-mcp-servers/guide/create-external-client-app.html).",
                    enableMarkdown: true);
            _clientSecret = brain.Builder
                .AddParameter("salesforce-client-secret", secret: true)
                .WithDescription(
                    "Client secret from the Salesforce External Client App configured for this server-side client.",
                    enableMarkdown: true);
            _redirectUri = brain.Builder
                .AddParameter("salesforce-redirect-uri")
                .WithDescription(
                    "HTTP loopback callback URI registered on the Salesforce External Client App, for example `http://localhost:41002/callback`.",
                    enableMarkdown: true);
        }

        public override void Apply<T>(IResourceBuilder<T> builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            if (!_salesforce)
            {
                return;
            }

            builder
                .WithEnvironment("DigitalBrain__Salesforce__ClientId", _clientId!)
                .WithEnvironment("DigitalBrain__Salesforce__ClientSecret", _clientSecret!)
                .WithEnvironment("DigitalBrain__Salesforce__RedirectUri", _redirectUri!);
        }
    }
}
