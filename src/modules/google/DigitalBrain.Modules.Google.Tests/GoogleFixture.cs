using DigitalBrain.Google;
using DigitalBrain.Mcp.Testing;
using DigitalBrain.Testing;

namespace DigitalBrain.Google.Tests;

public sealed class GoogleFixture : DigitalBrainFixture
{
    public const string GmailServerKey = "google.gmail";
    public const string GmailAccount = "reader@example.com";
    public const string SampleMessageId = "msg-intent-1";
    public const string SampleSubject = "Quarterly update";
    public const string SampleSender = "ops@acme.example";
    public const string SampleBody = "Pipeline is green.";

    protected override void Configure(DigitalBrainTestBuilder brain)
    {
        ArgumentNullException.ThrowIfNull(brain);
        brain.AddModule<GoogleModule>();
        brain.ConfigureMcpChatEdge();
    }
}
