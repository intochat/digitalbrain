using DigitalBrain.AI;
using DigitalBrain.AI.Scripted;
using Microsoft.Extensions.Options;
using DigitalBrain.Apps;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Kernel.Enforcement;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Modules.Apps.Tests.Unit;

// The create loop with scripted Author and Builder models: the request becomes a plain-language
// spec, a failing implementation is sent back with its failing scenarios, and the app is published
// once its tests run green.
public sealed class AppDraftFacts
{
    [Fact]
    public async Task FailedRebuildsKeepSourceFilesPairedWithTheLastPublishedBindings()
    {
        var runner = new ScriptedTestRunner();
        await using var brain = await StartAsync(TestContext.Current.CancellationToken, runner);
        var document = AppDocumentFacts.Document();
        var authored = System.Text.Json.Nodes.JsonNode.Parse(Authored(Spec))!;
        authored["document"] = System.Text.Json.Nodes.JsonNode.Parse(AppDocumentCodec.Encode(document));
        string Implementation(string marker, string path) => System.Text.Json.JsonSerializer.Serialize(new
        {
            settings = Array.Empty<object>(),
            files = new Dictionary<string, string> { ["tests.cs"] = marker, ["prompts/system.md"] = "Reply.", [path] = marker },
            behaviorSources = new[] { new { behaviorId = document.Behaviors[0].Id, sourcePaths = new[] { path } } },
        });
        await brain.Get<IScriptedLLM>("author").Script([authored.ToJsonString()]);
        await brain.Get<IScriptedLLM>("builder").Script([
            Implementation("// good", "behaviors/old.cs"),
            Implementation("// bad", "behaviors/new.cs"),
            Implementation("// bad", "behaviors/new.cs"),
            Implementation("// bad", "behaviors/new.cs"),
        ]);
        runner.BySourceMarker["// good"] = (0, "dbtest:pass Research saves");
        runner.BySourceMarker["// bad"] = (1, "dbtest:fail Research saves\tThe report was empty.");
        StampAlice();
        var draft = brain.Get<IAppDraft>("alice/drafts/" + Guid.NewGuid().ToString("N"));
        await draft.Draft("Research.");
        var published = await draft.Build();
        Assert.Equal(AppDraftStatus.Published, published.Draft.Status);
        await draft.SaveDocument(new(published.Draft.Revision, published.Draft.Document! with { Preamble = "Changed" }));
        var failed = await draft.Build();
        Assert.Equal(AppDraftStatus.Failed, failed.Draft.Status);
        Assert.Equal(published.Draft.Published, failed.FilesRevision);
        Assert.Equal("// good", failed.Files![failed.Draft.Document!.Behaviors[0].SourcePaths[0]]);
        Assert.NotEqual(failed.FilesRevision, failed.Verification!.Revision);
        Assert.False(failed.VerificationCurrent);
    }

    [Fact]
    public async Task StructuredBuildsPreserveTheDocumentAndEditsMakeResultsHistorical()
    {
        var ct = TestContext.Current.CancellationToken;
        var runner = new ScriptedTestRunner();
        await using var brain = await StartAsync(ct, runner);
        var document = AppDocumentFacts.Document();
        var authored = System.Text.Json.Nodes.JsonNode.Parse(Authored(Spec))!;
        authored["document"] = System.Text.Json.Nodes.JsonNode.Parse(AppDocumentCodec.Encode(document));
        await brain.Get<IScriptedLLM>("author").Script([authored.ToJsonString()]);
        await brain.Get<IScriptedLLM>("builder").Script([Built("// structured", "Reply.")]);
        runner.BySourceMarker["// structured"] = (0, "dbtest:pass Research saves");
        StampAlice();
        var draft = brain.Get<IAppDraft>("alice/drafts/" + Guid.NewGuid().ToString("N"));
        await draft.Draft("Research.");
        var built = await draft.Build();
        Assert.NotNull(built.Draft.Document);
        Assert.Equal(AppDocumentCodec.Hash(built.Draft.Document), built.Draft.VerifiedDocumentHash);
        var content = (await brain.Get<IPackage>(built.Draft.Published!.Package.ToString()).ReadRevision(built.Draft.Published.Revision)).Content;
        Assert.Equal(document.Behaviors[0].Id, AppDocumentCodec.Read(content).Document!.Behaviors[0].Id);
        Assert.Contains("Read observed pages.", Assert.Single(await brain.Get<IScriptedLLM>("builder").Prompts()));
        var edited = await draft.SaveDocument(new(built.Draft.Revision, document with { Preamble = "Changed" }));
        Assert.Null(edited.Draft.VerifiedDocumentHash);
        Assert.NotNull(edited.Verification);
    }

    [Fact]
    public async Task ConversionIsOnlyAProposalUntilExplicitlySaved()
    {
        await using var brain = await StartAsync(TestContext.Current.CancellationToken);
        await brain.Get<IScriptedLLM>("author").Script([Authored(Spec)]);
        StampAlice();
        var draft = brain.Get<IAppDraft>("alice/drafts/" + Guid.NewGuid().ToString("N"));
        var original = await draft.Draft("Shout.");
        var proposal = await draft.ProposeConversion(original.Draft.Revision);
        Assert.Equal("It shouts", Assert.Single(proposal.Scenarios).Name);
        Assert.Empty(proposal.Behaviors);
        Assert.Null((await draft.Read()).Draft.Document);
        Assert.Equal(original.Draft.Spec, (await draft.Read()).Draft.Spec);
    }

    [Fact]
    public async Task StructuredDraftsSurviveReactivationAndRejectStaleOrLegacyOverwrites()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartAsync(ct);
        await brain.Get<IScriptedLLM>("author").Script([Authored(Spec)]);
        StampAlice();
        var draft = brain.Get<IAppDraft>("alice/drafts/" + Guid.NewGuid().ToString("N"));
        var original = await draft.Draft("Shout.");
        var document = AppDocumentFacts.Document();
        var saved = await draft.SaveDocument(new(original.Draft.Revision, document));
        Assert.True(saved.Draft.Revision > original.Draft.Revision);
        await Assert.ThrowsAsync<AppDraftConflictException>(() => draft.SaveDocument(new(original.Draft.Revision, document with { Preamble = "Lost edit" })));
        await Assert.ThrowsAsync<InvalidOperationException>(() => draft.EditSpec("Overwrite"));
        await brain.DeactivateAsync(draft, ct);
        Assert.Equal(document.Behaviors[0].Id, (await draft.Read()).Draft.Document!.Behaviors[0].Id);
        Assert.Null((await draft.Read()).Draft.VerifiedDocumentHash);
    }

    [Fact]
    public async Task ARegisteredRuntimeCanBeAuthoredFromItsOwnDescription()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartAsync(ct, runtime: new DescribedRuntime());
        await brain.Get<IScriptedLLM>("author").Script([Authored(Spec).Replace("\"prompt\"", "\"test-echo\"", StringComparison.Ordinal)]);
        StampAlice();
        var draft = brain.Get<IAppDraft>("alice/drafts/" + Guid.NewGuid().ToString("N"));
        var result = await draft.Draft("Echo my message.");
        Assert.Equal("test-echo", result.Draft.Runtime);
        Assert.Contains(DescribedRuntime.Description, Assert.Single(await brain.Get<IScriptedLLM>("author").Prompts()), StringComparison.Ordinal);
    }

    private sealed class DescribedRuntime : IAppRuntime
    {
        public const string Description = "test-echo answers input without settings or files.";
        public string Name => "test-echo";
        public string AuthoringDescription => Description;
        public Task<string> Answer(AppRuntimeRequest request, CancellationToken cancellationToken) => Task.FromResult(request.Input);
    }

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
        var runner = new ScriptedTestRunner();
        await using var brain = await StartAsync(ct, runner);
        await brain.Get<IScriptedLLM>("author").Script([Authored(Spec)]);
        await brain.Get<IScriptedLLM>("builder").Script([
            Built("// proof v1", "Whisper the answer."),
            Built("// proof v2", "Shout the answer in capitals."),
        ]);
        runner.BySourceMarker["// proof v1"] = (1, "dbtest:fail It shouts\tThe answer was \"hello\".");
        runner.BySourceMarker["// proof v2"] = (0, "dbtest:pass It shouts");
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
        await brain.DeactivateAsync(draft, ct);
        var restored = await draft.Read();
        Assert.Equal(AppDraftStatus.Published, restored.Draft.Status);
        Assert.Equal([false, true], restored.Draft.Attempts.Select(attempt => attempt.Green));
        Assert.Equal(built.Draft.Published, restored.Draft.Published);
    }

    [Fact]
    public async Task ABuilderThatCallsAToolGetsItsResultAndStillPublishes()
    {
        var ct = TestContext.Current.CancellationToken;
        var runner = new ScriptedTestRunner();
        await using var brain = await StartAsync(ct, runner);
        await brain.Get<IScriptedLLM>("author").Script([Authored(Spec)]);
        // The first scripted reply is a tool call; the loop answers it and asks again.
        await brain.Get<IScriptedLLM>("builder").Script([
            """{"tool":"check_csharp","arguments":{"files":{"tests.cs":"var answer = 1;\nConsole.WriteLine(answer);"}}}""",
            Built("// proof v1", "Shout the answer in capitals."),
        ]);
        runner.BySourceMarker["// proof v1"] = (0, "dbtest:pass It shouts");
        StampAlice();
        var draft = brain.Get<IAppDraft>("alice/drafts/" + Guid.NewGuid().ToString("N"));
        await draft.Draft("Shout back.");

        var built = await draft.Build();

        Assert.Equal(AppDraftStatus.Published, built.Draft.Status);
    }

    [Fact]
    public async Task AnImplementationWithoutTestsIsAFailedAttempt()
    {
        var ct = TestContext.Current.CancellationToken;
        var runner = new ScriptedTestRunner();
        await using var brain = await StartAsync(ct, runner);
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
    public async Task BuildRefusesUpfrontWhenTheHostCannotRunTests()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartAsync(ct, canRun: false);
        await brain.Get<IScriptedLLM>("author").Script([Authored(Spec)]);
        StampAlice();
        var draft = brain.Get<IAppDraft>("alice/drafts/" + Guid.NewGuid().ToString("N"));
        await draft.Draft("Shout back.");

        await Assert.ThrowsAsync<InvalidOperationException>(draft.Build);

        Assert.Equal(AppDraftStatus.Drafted, (await draft.Read()).Draft.Status);
        Assert.Empty(await brain.Get<IScriptedLLM>("builder").Prompts());
        Assert.DoesNotContain("csharp uses sandbox contracts.",
            Assert.Single(await brain.Get<IScriptedLLM>("author").Prompts()), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DraftsAreListedForTheirOwnerNewestFirst()
    {
        var ct = TestContext.Current.CancellationToken;
        var runner = new ScriptedTestRunner();
        await using var brain = await StartAsync(ct, runner);
        await brain.Get<IScriptedLLM>("author").Script([Authored(Spec), Authored(Spec)]);
        await brain.Get<IScriptedLLM>("builder").Script([Built("// proof listed", "Shout.")]);
        runner.BySourceMarker["// proof listed"] = (0, "dbtest:pass It shouts");
        StampAlice();
        var first = Guid.NewGuid().ToString("N");
        var second = Guid.NewGuid().ToString("N");
        await brain.Get<IAppDraft>("alice/drafts/" + first).Draft("Shout back.");
        await brain.Get<IAppDraft>("alice/drafts/" + second).Draft("Shout back louder.");

        await brain.Get<IAppDraft>("alice/drafts/" + second).Build();
        var drafts = await brain.Get<IAppDrafts>("alice").List();

        Assert.Equal([second, first], drafts.Select(entry => entry.Id));
        Assert.Equal(AppDraftStatus.Published, drafts[0].Status);
        Assert.Equal(AppDraftStatus.Drafted, drafts[1].Status);
        Assert.Equal("Shouter", drafts[0].Title);
    }

    [Fact]
    public async Task AFailedIndexWriteNeverFailsTheDraft()
    {
        var ct = TestContext.Current.CancellationToken;
        var runner = new ScriptedTestRunner();
        await using var brain = await StartAsync(ct, runner);
        await brain.Get<IScriptedLLM>("author").Script([Authored(Spec)]);
        StampAlice();
        // "alice" alone has no draft-id segment, so recording into the index throws; the draft
        // itself must still land, because the index is only a read model.
        var drafted = await brain.Get<IAppDraft>("alice").Draft("Shout back.");

        Assert.Equal(AppDraftStatus.Drafted, drafted.Draft.Status);
        Assert.Empty(await brain.Get<IAppDrafts>("alice").List());
    }

    [Fact]
    public async Task ARevisionKeepsTheDraftsNameBecauseTheAuthorIsToldIt()
    {
        var ct = TestContext.Current.CancellationToken;
        var runner = new ScriptedTestRunner();
        await using var brain = await StartAsync(ct, runner);
        await brain.Get<IScriptedLLM>("author").Script([Authored(Spec), Authored(Spec + "\n\nAlso ends with an exclamation mark.")]);
        StampAlice();
        var draft = brain.Get<IAppDraft>("alice/drafts/" + Guid.NewGuid().ToString("N"));
        await draft.Draft("Shout back.");

        await draft.Revise("Also end with an exclamation mark.");

        Assert.Contains("Current name: shouter", (await brain.Get<IScriptedLLM>("author").Prompts())[1], StringComparison.Ordinal);
    }

    private static string Authored(string spec) => System.Text.Json.JsonSerializer.Serialize(new
    {
        name = "shouter",
        title = "Shouter",
        description = "Shouts back.",
        runtime = "prompt",
        spec,
    });

    private static string Built(string testsMarker, string systemPrompt) => System.Text.Json.JsonSerializer.Serialize(new
    {
        settings = new[] { new { name = "Model", description = "Who answers.", @default = "IGemma4" } },
        files = new Dictionary<string, string> { ["tests.cs"] = testsMarker, ["prompts/system.md"] = systemPrompt },
    });

    private static void StampAlice() => CallerContextStamper.Stamp(new CallerContext
    {
        PrincipalId = "alice",
        AccountId = "account-alice",
        BrainId = "workspace-alice",
        Kind = CallerKind.User,
        StampedBy = TrustedEdge.AuthenticatedHttp,
    });

    private static Task<UnitBrain> StartAsync(CancellationToken ct, DigitalBrain.Apps.ITestScriptRunner? runner = null, bool canRun = true, IAppRuntime? runtime = null) => UnitTest.Create()
        .WithModule<AIModule>()
        .WithModule<AppsModule>()
        // Extra keys join the host configuration; replacing the IConfiguration singleton would
        // erase the rest of the host's configuration (the master key included).
        .WithExecution(new TestExecutionOptions
        {
            PrivateConfiguration = new Dictionary<string, string?>
            {
                ["DigitalBrain:Apps:AuthorModel"] = IScriptedLLM.ModelPrefix + "author",
                ["DigitalBrain:Apps:BuilderModel"] = IScriptedLLM.ModelPrefix + "builder",
            },
        })
        .ConfigureSilo(silo =>
        {
            if (runtime is not null) { silo.Services.AddSingleton(runtime); }
            silo.Services.AddSingleton<DigitalBrain.Apps.ITestScriptRunner>(_ => runner ?? new ScriptedTestRunner());
            silo.Services.AddSingleton<IScriptSandbox>(new DraftSandbox(canRun));
        })
        .StartAsync(ct);
    private sealed class DraftSandbox(bool canRun) : IScriptSandbox
    {
        public bool CanRun => canRun;
        public string AuthoringDescription => "csharp uses sandbox contracts.";
        public Task<ScriptContractCatalog> ReadContracts(IReadOnlyList<string> modules, CancellationToken cancellationToken)
            => Task.FromResult(new ScriptContractCatalog([], [], ""));
        public ScriptCompilationCheck Check(IReadOnlyDictionary<string, string> files) => new(true, []);
    }
}
