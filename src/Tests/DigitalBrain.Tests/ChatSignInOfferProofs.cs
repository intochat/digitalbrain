using DigitalBrain.Abstractions;
using DigitalBrain.Chat;
using DigitalBrain.Modules.Sdk.Mcp;
using DigitalBrain.Tests.Harness;
using Xunit;

namespace DigitalBrain.Tests;

[Collection(BrainCollection.Name)]
public sealed class ChatSignInOfferProofs(BrainClusterFixture fixture)
{
    [Fact]
    public async Task AuthorizationRequiredOffersASignInButtonIntoTheChat()
    {
        var brain = fixture.BrainFor("chat-signin-offer");
        var chat = NeuronId.For<IChat>(brain.Owner, "main");
        var command = CommandId.New();
        var signInUrl = new Uri("https://login.salesforce.com/services/oauth2/authorize?state=sf1");

        await brain.FireAsync(
            chat,
            new AuthorizationRequired(
                command,
                "salesforce",
                "Salesforce",
                signInUrl,
                "sf1"),
            TestContext.Current.CancellationToken);

        await Journals.WaitForAsync(
            brain, chat, JournalKind.Outgoing,
            delivery => delivery.Synapse is Responded { Buttons.Length: > 0 } offered
                && offered.Buttons![0].Label == "Sign in via Salesforce"
                && offered.Buttons[0].Action == signInUrl.AbsoluteUri);

        var transcript = await brain.GetGrainProxy<IChat>("main").Read();
        Assert.Contains(
            transcript.Turns,
            turn => turn.Buttons is { Length: > 0 } buttons
                && buttons[0].Label == "Sign in via Salesforce"
                && buttons[0].Action == signInUrl.AbsoluteUri);
    }

    [Fact]
    public async Task BeginningMcpAuthorizationOffersTheSignInButtonIntoMainChat()
    {
        var brain = fixture.BrainFor("mcp-begin-signin-chat");
        var chat = NeuronId.For<IChat>(brain.Owner, "main");
        var command = CommandId.New();
        var authorization = brain.GetGrainProxy<IMcpAuthorization>(IMcpAuthorization.DefaultInstanceName);

        var required = await authorization.Begin(
            new BeginMcpAuthorization(
                command,
                "salesforce",
                "Salesforce",
                new Uri("https://login.salesforce.com/services/oauth2/authorize?state=begin1"),
                "begin1"),
            TestContext.Current.CancellationToken);

        await Journals.WaitForAsync(
            brain, chat, JournalKind.Outgoing,
            delivery => delivery.Synapse is Responded { Buttons.Length: > 0 } offered
                && offered.Buttons![0].Label == "Sign in via Salesforce"
                && offered.Buttons[0].Action == required.SignInUrl.AbsoluteUri);
    }
}
