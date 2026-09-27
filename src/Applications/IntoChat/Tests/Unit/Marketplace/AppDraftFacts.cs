using DigitalBrain.AI;
using DigitalBrain.AI.Scripted;
using DigitalBrain.Apps;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Core.Enforcement;
using DigitalBrain.Specs;
using IntoChat.Marketplace;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IntoChat.Tests.Unit.Marketplace;

// The create loop with scripted Author and Builder models: the request becomes scenarios, a failing
// implementation is sent back with its failing steps, and the app is published once they pass.
public sealed class AppDraftFacts
{
    private const string Spec = """"
        Feature: Shouter
          Scenario: It shouts
            Given the scripted model "voice" replies:
              """
              HELLO!
              """
            And the setting "Model" is the scripted model "voice"
            When I ask "hello"
            Then the answer is "HELLO!"
            And the scripted model "voice" was told "Shout"
        """";

    [Fact]
    public async Task ARequestBecomesAPublishedAppOnceItsScenariosPass()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartAsync(ct);
        await brain.Get<IScriptedLLM>("author").Script([Authored(Spec)]);
        await brain.Get<IScriptedLLM>("builder").Script([
            """{"settings":[{"name":"Model","description":"Who answers.","default":"IGemma4"}],"files":{"prompts/system.md":"Whisper the answer."}}""",
            """{"settings":[{"name":"Model","description":"Who answers.","default":"IGemma4"}],"files":{"prompts/system.md":"Shout the answer in capitals."}}""",
        ]);
        StampAlice();
        var draft = brain.Get<IAppDraft>("alice/drafts/" + Guid.NewGuid().ToString("N"));

        var drafted = await draft.Draft("An app that shouts back whatever I say.");
        Assert.Equal(AppDraftStatus.Drafted, drafted.Draft.Status);
        Assert.Equal("prompt", drafted.Draft.Runtime);
        Assert.True(drafted.Feature?.FullyBound);

        var built = await draft.Build();

        Assert.Equal(AppDraftStatus.Published, built.Draft.Status);
        Assert.Equal([false, true], built.Draft.Attempts.Select(attempt => attempt.Green));
        Assert.Contains("was never told \"Shout\"", built.Draft.Attempts[0].Failures);
        var builderPrompts = await brain.Get<IScriptedLLM>("builder").Prompts();
        Assert.Contains("was never told", builderPrompts[1]);
        var listing = Assert.Single(await brain.Get<IPackageDirectory>(PackageDirectory.Key).List());
        Assert.Equal("alice/shouter", listing.Package.ToString());
    }

    [Fact]
    public async Task StepsTheBrainDoesNotUnderstandGoBackToTheAuthor()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartAsync(ct);
        var unbound = "Feature: Shouter\n  Scenario: It shouts\n    When I ask \"hello\"\n    Then the reply is very loud";
        var fixedSpec = "Feature: Shouter\n  Scenario: It shouts\n    When I ask \"hello\"\n    Then the answer mentions \"HELLO\"";
        await brain.Get<IScriptedLLM>("author").Script([Authored(unbound), Authored(fixedSpec)]);
        StampAlice();

        var drafted = await brain.Get<IAppDraft>("alice/drafts/" + Guid.NewGuid().ToString("N")).Draft("Shout back.");

        Assert.True(drafted.Feature?.FullyBound);
        Assert.Contains("Then the reply is very loud", (await brain.Get<IScriptedLLM>("author").Prompts())[1]);
    }

    [Fact]
    public async Task ARevisionKeepsTheDraftsNameBecauseTheAuthorIsToldIt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartAsync(ct);
        var spec = "Feature: Shouter\n  Scenario: It shouts\n    When I ask \"hello\"\n    Then the answer mentions \"HELLO\"";
        await brain.Get<IScriptedLLM>("author").Script([Authored(spec), Authored(spec + "\n    And the answer mentions \"!\"")]);
        StampAlice();
        var draft = brain.Get<IAppDraft>("alice/drafts/" + Guid.NewGuid().ToString("N"));
        await draft.Draft("Shout back.");

        await draft.Revise("Also end with an exclamation mark.");

        Assert.Contains("Current name: shouter", (await brain.Get<IScriptedLLM>("author").Prompts())[1]);
    }

    private static string Authored(string feature) => System.Text.Json.JsonSerializer.Serialize(new
    {
        name = "shouter", title = "Shouter", description = "Shouts back.", runtime = "prompt", feature,
    });

    private static void StampAlice() => CallerContextStamper.Stamp(new CallerContext
    {
        PrincipalId = "alice", AccountId = "account-alice", WorkspaceId = "workspace-alice",
        Kind = CallerKind.User, StampedBy = TrustedEdge.AuthenticatedHttp,
    });

    private static Task<UnitBrain> StartAsync(CancellationToken ct) => UnitTest.Create()
        .WithModule<AIModule>()
        .WithModule<SpecsModule>()
        .WithModule<AppsModule>()
        .ConfigureSilo(silo =>
        {
            silo.Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["IntoChat:Apps:AuthorModel"] = IScriptedLLM.ModelPrefix + "author",
                ["IntoChat:Apps:BuilderModel"] = IScriptedLLM.ModelPrefix + "builder",
            }).Build());
            silo.Services.AddAppRuntime<GroupChatRuntime>();
            silo.Services.AddAppRuntime<PromptRuntime>();
            silo.Services.AddSingleton<StepLibrary, ModelSteps>();
            silo.Services.AddSingleton<StepLibrary, GroupChatSteps>();
        })
        .StartAsync(ct);
}
