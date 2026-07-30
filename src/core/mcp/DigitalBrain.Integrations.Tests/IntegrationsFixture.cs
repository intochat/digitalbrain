using DigitalBrain.Abstractions;
using DigitalBrain.AccountEnrichment;
using DigitalBrain.Behaviors;
using DigitalBrain.Flutter;
using DigitalBrain.Google;
using DigitalBrain.Salesforce;
using DigitalBrain.Mcp.Testing;
using DigitalBrain.Testing;

namespace DigitalBrain.Integrations.Tests;

public sealed class IntegrationsFixture : DigitalBrainFixture
{
    public const string GmailServerKey = "google.gmail";
    public const string SalesforceServerKey = "salesforce";
    public const string GmailGetMessageTool = "get_message";
    public const string SessionName = ISessionNeuron.InstanceName;
    public const string ShellName = "desk";
    public const string EnrichmentSceneKey = "enrichment";
    public const string EnrichmentSceneTitle = "Account enrichment";
    public const string SampleAccountId = "001xx000003DGbYAAW";
    public const string SampleGmailAccount = "reader@example.com";
    public const string SampleMessageId = "msg-enrich-1";
    public const string SampleSubject = "Acme pipeline update";
    public const string SampleSender = "ops@acme.example";
    public const string SampleBody = "Q3 forecast closed green.";

    public static string SampleEnrichmentDescription
        => $"Email from {SampleSender}: {SampleSubject}\n{SampleBody}";

    public static NeuronId SessionOf(TestBrain test)
    {
        ArgumentNullException.ThrowIfNull(test);
        return ISessionNeuron.ForOwner(test.Client.Owner);
    }

    public static SalesforceMutationApproval Approval(TestBrain test, CommandId commandId, string fingerprint)
    {
        ArgumentNullException.ThrowIfNull(test);
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
        return new(Guid.NewGuid(), commandId, fingerprint, SessionOf(test), test.Clock.UtcNow);
    }

    protected override void Configure(DigitalBrainTestBuilder brain)
    {
        ArgumentNullException.ThrowIfNull(brain);
        brain.AddModule<BehaviorsModule>();
        brain.AddModule<GoogleModule>();
        brain.AddModule<SalesforceModule>();
        brain.AddModule<EnrichmentModule>();
        brain.AddModule<FlutterModule>();
        brain.AddModule<IntegrationsHarnessModule>();
        brain.ConfigureMcpEdge();
    }
}
