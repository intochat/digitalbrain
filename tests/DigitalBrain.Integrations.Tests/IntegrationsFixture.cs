using DigitalBrain.AccountEnrichment;
using DigitalBrain.Google;
using DigitalBrain.Salesforce;
using DigitalBrain.Testing;

namespace DigitalBrain.Integrations.Tests;

public sealed class IntegrationsFixture : DigitalBrainFixture
{
    protected override void Configure(DigitalBrainTestBuilder brain)
    {
        ArgumentNullException.ThrowIfNull(brain);
        brain.AddModule<GoogleModule>();
        brain.AddModule<SalesforceModule>();
        brain.AddModule<EnrichmentModule>();
        brain.AddModule<IntegrationsHarnessModule>();
        brain.ConfigureMcpEdge();
    }
}
