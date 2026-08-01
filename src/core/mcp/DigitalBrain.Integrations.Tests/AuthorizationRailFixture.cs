using DigitalBrain.AccountEnrichment;
using DigitalBrain.Flutter;
using DigitalBrain.Google;
using DigitalBrain.Mcp;
using DigitalBrain.Salesforce;
using DigitalBrain.Mcp.Testing;
using DigitalBrain.Testing;

namespace DigitalBrain.Integrations.Tests;

public sealed class AuthorizationRailFixture : DigitalBrainFixture
{
    public const string PublicSignInBase = "https://ui.test.digitalbrain.local/";

    protected override void Configure(DigitalBrainTestBuilder brain)
    {
        ArgumentNullException.ThrowIfNull(brain);
        brain.AddModule<GoogleModule>();
        brain.AddModule<SalesforceModule>();
        brain.AddModule<EnrichmentModule>();
        brain.AddModule<FlutterModule>();
        brain.AddModule<IntegrationsHarnessModule>();
        IntegrationsGmailHosts.ResetRuntimeState();
        IntegrationsGmailHosts.ApplyConfiguration(brain);
        brain.ConfigureMcpEdge();
        brain.Configure(McpRuntimeHosting.PublicSignInBaseKey, PublicSignInBase);
    }
}
