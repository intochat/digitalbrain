using DigitalBrain.Mcp.Testing;
using DigitalBrain.Salesforce;
using DigitalBrain.Testing;

namespace DigitalBrain.Salesforce.Tests;

public sealed class SalesforceFixture : DigitalBrainFixture
{
    public const string ServerKey = "salesforce";
    public const string Connection = "salesforce";
    public const string SampleAccountId = "001xx000003DGbYAAW";
    public const string SampleDescription = "Email from ops@acme.example: pipeline green";

    protected override void Configure(DigitalBrainTestBuilder brain)
    {
        ArgumentNullException.ThrowIfNull(brain);
        brain.AddModule<SalesforceModule>();
        brain.ConfigureMcpChatEdge();
    }
}
