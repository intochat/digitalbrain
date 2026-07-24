using DigitalBrain.Google;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.ModuleTests;

public sealed class GoogleContracts(ModuleFixture fixture)
{
    [Fact]
    public async Task GmailMapsTheTypedSouthboundMessage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        test.ConfigureModuleParameters();
        var driver = test.Neuron<IModuleDriver>("gmail-driver");
        var gmail = test.Neuron<IGmail>("ada@example.test");
        var observed = driver.Outgoing.NextAsync<GmailRead>(
            cancellationToken);

        await test.Client.SendAsync<IModuleDriver>(
            "gmail-driver",
            new ReadGmail(gmail.Id, "message-42"));
        var message = (await observed).Synapse.Message;

        Assert.Equal("message-42", message.Id);
        Assert.Equal("Module testing", message.Subject);
        Assert.Equal("ada@example.test", message.Sender);
        Assert.Equal("Typed Gmail mapping", message.PlaintextBody);
        var call = Assert.Single(test.Mcp().Calls);
        Assert.Equal("get_message", call.Tool);
        Assert.Equal(
            "message-42",
            call.Arguments.GetProperty("messageId").GetString());
    }
}
