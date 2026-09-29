using DigitalBrain.AI;
using DigitalBrain.AI.Scripted;
using DigitalBrain.Apps;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Core.Enforcement;
using IntoChat.Marketplace;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IntoChat.Tests.Unit.Marketplace;

// The create loop with scripted Author and Builder models: the request becomes a plain-language
// spec, a failing implementation is sent back with its failing scenarios, and the app is published
// once its tests run green.
public sealed class AppDraftFacts
{
    private const string Spec = """
        # Shouter

        Shouts back whatever you say.

        ## Scenario: It shouts

        Asking "hello" answers "HELLO!".
        """;

    [Fact]
    public async Task ARequestBecomesAPublishedAppOnceItsTestsPass()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartAsync(ct);
        await brain.Get<IScriptedLLM>("author").Script([Authored(Spec)]);
        await brain.Get<IScriptedLLM>("builder").Script([
            Built("// proof v1", "Whisper the answer."),
            Built("// proof v2", "Shout the answer in capitals."),
        ]);
        ScriptedTestRunner.BySourceMarker["// proof v1"] = (1, "dbtest:fail It shouts\tThe answer was \"hello\".");
        ScriptedTestRunner.BySourceMarker["// proof v2"] = (0, "dbtest:pass It shouts");
        StampAlice();
        var draft = brain.Get<IAppDraft>("alice/drafts/" + Guid.NewGuid().ToString("N"));

        var drafted = await draft.Draft("An app that shouts back whatever I say.");
        Assert.Equal(AppDraftStatus.Drafted, drafted.Draft.Status);
        Assert.Equal("prompt", drafted.Draft.Runtime);
        Assert.Contains("## Scenario: It shouts", drafted.Draft.Spec, StringComparison.Ordinal);

        var built = await draft.Build();

        Assert.Equal(AppDraftStatus.Published, built.Draft.Status);
        Assert.Equal([false, true], built.Draft.Attempts.Select(attempt => attempt.Green));
        Assert.Contains("The answer was \"hello\".", built.Draft.Attempts[0].Failures, StringComparison.Ordinal);
        Assert.True(built.Verification?.Green);
        var builderPrompts = await brain.Get<IScriptedLLM>("builder").Prompts();
        Assert.Contains("The answer was \"hello\".", builderPrompts[1], StringComparison.Ordinal);
        var listing = Assert.Single(await brain.Get<IPackageDirectory>(PackageDirectory.Key).List());
        Assert.Equal("alice/shouter", listing.Package.ToString());
    }

    [Fact]
    public async Task AnImplementationWithoutTestsIsAFailedAttempt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartAsync(ct);
        await brain.Get<IScriptedLLM>("author").Script([Authored(Spec)]);
        await brain.Get<IScriptedLLM>("builder").Script([
            """{"settings":[],"files":{"prompts/system.md":"Shout."}}""",
            """{"settings":[],"files":{"prompts/system.md":"Shout!"}}""",
            """{"settings":[],"files":{"prompts/system.md":"Shout!!"}}""",
        ]);
        StampAlice();
        var draft = brain.Get<IAppDraft>("alice/drafts/" + Guid.NewGuid().ToString("N"));
        await draft.Draft("Shout back.");

        var built = await draft.Build();

        Assert.Equal(AppDraftStatus.Failed, built.Draft.Status);
        Assert.All(built.Draft.Attempts, attempt => Assert.Contains("must include tests.cs", attempt.Failures, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ARevisionKeepsTheDraftsNameBecauseTheAuthorIsToldIt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartAsync(ct);
        await brain.Get<IScriptedLLM>("author").Script([Authored(Spec), Authored(Spec + "\n\nAlso ends with an exclamation mark.")]);
        StampAlice();
        var draft = brain.Get<IAppDraft>("alice/drafts/" + Guid.NewGuid().ToString("N"));
        await draft.Draft("Shout back.");

        await draft.Revise("Also end with an exclamation mark.");

        Assert.Contains("Current name: shouter", (await brain.Get<IScriptedLLM>("author").Prompts())[1], StringComparison.Ordinal);
    }

    private static string Authored(string spec) => System.Text.Json.JsonSerializer.Serialize(new
    {
        name = "shouter", title = "Shouter", description = "Shouts back.", runtime = "prompt", spec,
    });

    private static string Built(string testsMarker, string systemPrompt) => System.Text.Json.JsonSerializer.Serialize(new
    {
        settings = new[] { new { name = "Model", description = "Who answers.", @default = "IGemma4" } },
        files = new Dictionary<string, string> { ["tests.cs"] = testsMarker, ["prompts/system.md"] = systemPrompt },
    });

    private static void StampAlice() => CallerContextStamper.Stamp(new CallerContext
    {
        PrincipalId = "alice", AccountId = "account-alice", WorkspaceId = "workspace-alice",
        Kind = CallerKind.User, StampedBy = TrustedEdge.AuthenticatedHttp,
    });

    private static Task<UnitBrain> StartAsync(CancellationToken ct) => UnitTest.Create()
        .WithModule<AIModule>()
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
            silo.Services.AddSingleton<DigitalBrain.Apps.ITestScriptRunner, ScriptedTestRunner>();
        })
        .StartAsync(ct);
}
