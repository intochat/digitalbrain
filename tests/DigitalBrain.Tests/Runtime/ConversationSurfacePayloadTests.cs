using DigitalBrain.Core;
using DigitalBrain.Core.Runtime;

namespace DigitalBrain.Tests.Runtime;

public sealed class ConversationSurfacePayloadTests
{
    [Fact]
    public void Build_omits_a_structurally_invalid_persisted_action()
    {
        var conversation = new InoConversationSnapshot(
            "ino-" + new string('a', 64),
            3,
            [new InoConversationTurn("command-1", "assistant", "Connect Salesforce to continue.", InoConversationStates.Succeeded)],
            [new InoConversationOperation(
                "command-1",
                "connect salesforce",
                InoConversationStates.Succeeded,
                null,
                false,
                DateTimeOffset.UtcNow,
                new ToolAction("openUrl", "Connect Salesforce", "https://login.salesforce.com/services/oauth2/authorize?raw=legacy"),
                null,
                null,
                null)]);

        var payload = ConversationSurfacePayload.Build(conversation);

        var operation = payload.GetProperty("data").GetProperty("operation");
        Assert.False(operation.TryGetProperty("action", out _));
        Assert.DoesNotContain("login.salesforce.com", payload.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public void Build_keeps_a_structurally_valid_internal_action()
    {
        var conversation = new InoConversationSnapshot(
            "ino-" + new string('a', 64),
            3,
            [new InoConversationTurn("command-1", "assistant", "Connect Salesforce to continue.", InoConversationStates.AwaitingAuthorization)],
            [new InoConversationOperation(
                "command-1",
                "connect salesforce",
                InoConversationStates.AwaitingAuthorization,
                null,
                false,
                DateTimeOffset.UtcNow,
                new ToolAction(
                    "openUrl",
                    "Connect Salesforce",
                    OAuthCallbackPaths.CreateInternalStartPath(OAuthCallbackPaths.SalesforceProvider, new string('a', 32))),
                null,
                null,
                null)]);

        var payload = ConversationSurfacePayload.Build(conversation);

        var action = payload.GetProperty("data").GetProperty("operation").GetProperty("action");
        Assert.Equal("Connect Salesforce", action.GetProperty("label").GetString());
    }
}
