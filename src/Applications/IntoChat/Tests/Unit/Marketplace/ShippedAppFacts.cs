using DigitalBrain.AI;
using DigitalBrain.AI.GroupChat;
using DigitalBrain.AI.Scripted;
using DigitalBrain.Apps;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Core.Enforcement;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IntoChat.Tests.Unit.Marketplace;

// Every shipped app carries its spec and its tests, and the configuration apps' deterministic
// scenarios pass against the real runtimes here, natively — the same assertions their tests.cs
// makes in the production gate.
public sealed class ShippedAppFacts
{
    private static readonly EmbeddedShippedAppSource Source = new(typeof(Program).Assembly, "IntoChat.ShippedApps/", "intochat");
    [Fact]
    public void EveryFirstPartyPackageShips()
        => Assert.Equal(["assistant", "customer-researcher", "group-chat", "settings", "word-count"], Source.Load().Select(app => app.Package.Name).Order());

    [Fact]
    public void EveryShippedProgramAndTestsFileParsesAsCSharp()
    {
        foreach (var app in Source.Load())
        {
            var sources = new Dictionary<string, string> { [PackageContent.TestsPath] = app.Content.File(PackageContent.TestsPath)! };
            foreach (var (path, program) in app.Content.Programs()) { sources[path] = program; }
            foreach (var (path, source) in sources)
            {
                // dotnet run strips the #: file-based-app directives before compiling.
                var stripped = string.Join("\n", source.Split('\n').Where(line => !line.TrimStart().StartsWith("#:", StringComparison.Ordinal)));
                var errors = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree
                    .ParseText(stripped, cancellationToken: TestContext.Current.CancellationToken)
                    .GetDiagnostics(TestContext.Current.CancellationToken)
                    .Where(diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).ToArray();
                Assert.True(errors.Length == 0, $"{app.Package}/{path}: {string.Join("; ", errors.Take(3).Select(error => error.ToString()))}");
            }
        }
    }

    // The researcher is the proof that a first-party app is an ordinary package: every contract its
    // scripts compile against is a platform module a user's own app could name, so its tests verify
    // on a host where no CustomerResearcher module is composed.
    [Fact]
    public void TheResearcherPackageUsesOnlyPlatformContracts()
    {
        string[] platform =
        [
            "/brain/src/Modules/DigitalBrain/Apps/DigitalBrain.Modules.Apps.Contracts/DigitalBrain.Modules.Apps.Contracts.csproj",
            "/brain/src/Modules/Google/Flutter/DigitalBrain.Modules.Flutter.Contracts/DigitalBrain.Modules.Flutter.Contracts.csproj",
            "/brain/src/Modules/Microsoft/Playwright/DigitalBrain.Modules.Microsoft.Playwright.Contracts/DigitalBrain.Modules.Microsoft.Playwright.Contracts.csproj",
            "/brain/src/Modules/AI/DigitalBrain.Modules.AI.Contracts/DigitalBrain.Modules.AI.Contracts.csproj",
            "/brain/src/Modules/Postgres/DigitalBrain.Modules.Postgres.Contracts/DigitalBrain.Modules.Postgres.Contracts.csproj",
        ];
        var researcher = Source.Load().Single(app => app.Package.Name == "customer-researcher");
        Assert.True(string.IsNullOrEmpty(researcher.Content.Source), "The researcher still carries a legacy app.cs.");
        Assert.Equal(["behaviors/research.cs", "behaviors/surface.cs"], researcher.Content.Programs().Keys);
        var sources = researcher.Content.Programs().Values.Append(researcher.Content.File(PackageContent.TestsPath)!);
        Assert.All(sources, source =>
        {
            Assert.DoesNotContain("CustomerResearcher.Contracts", source, StringComparison.Ordinal);
            foreach (var directive in source.Split('\n').Select(line => line.Trim()).Where(line => line.StartsWith("#:project", StringComparison.Ordinal)))
            { Assert.Contains(directive["#:project".Length..].Trim(), platform); }
        });
    }

    [Fact]
    public void EveryShippedAppCarriesSpecAndTests()
        => Assert.All(Source.Load(), app =>
        {
            Assert.False(string.IsNullOrWhiteSpace(app.Content.File(PackageContent.SpecPath)), $"{app.Package} has no {PackageContent.SpecPath}.");
            Assert.False(string.IsNullOrWhiteSpace(app.Content.File(PackageContent.TestsPath)), $"{app.Package} has no {PackageContent.TestsPath}.");
        });

    [Fact]
    public async Task TheAssistantAnswersWithItsSystemPromptAgainstTheRealRuntime()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartAsync(ct);
        var (app, scope) = await Install(brain, "assistant");
        var helper = brain.Get<IScriptedLLM>(scope + "/helper");
        await helper.Script(["Paris is the capital of France."]);
        await app.Configure(new ConfigureApp(Guid.NewGuid(), new Dictionary<string, string> { ["Model"] = IScriptedLLM.ModelPrefix + scope + "/helper" }));

        var (answer, _) = await Ask(app, "What is the capital of France?", ct);

        Assert.Equal("Paris is the capital of France.", answer);
        Assert.Contains(await helper.Prompts(), prompt => prompt.Contains("at most three clear sentences", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task TheGroupChatTakesTurnsAndStopsOnceAgreedAgainstTheRealRuntime()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartAsync(ct);
        var (app, scope) = await Install(brain, "group-chat");
        await brain.Get<IScriptedLLM>(scope + "/luna").Script([
            "A smart dog bowl.",
            "AGREE: the bowl with a feeding log.",
            "A smart dog bowl that logs every meal.",
        ]);
        await brain.Get<IScriptedLLM>(scope + "/gemma").Script([
            "Add a feeding log to the bowl.",
            "AGREE: bowl plus log.",
        ]);
        await app.Configure(new ConfigureApp(Guid.NewGuid(), new Dictionary<string, string>
        {
            ["LunaModel"] = IScriptedLLM.ModelPrefix + scope + "/luna",
            ["GemmaModel"] = IScriptedLLM.ModelPrefix + scope + "/gemma",
        }));

        var (answer, invocationId) = await Ask(app, "Name one product idea for dog owners", ct);

        Assert.Equal("A smart dog bowl that logs every meal.", answer);
        var discussion = await brain.Get<IGroupChat>(GroupChatRuntime.ChatKey(scope + "/app", invocationId)).Read();
        var speakers = discussion.Turns.Select(turn => turn.Speaker).ToArray();
        Assert.True(speakers.Length >= 2 && speakers.SequenceEqual(speakers.Select((_, index) => index % 2 == 0 ? "Luna" : "Gemma")),
            $"The turns went {string.Join(", ", speakers)}.");
        Assert.Contains(await brain.Get<IScriptedLLM>(scope + "/gemma").Prompts(), prompt => prompt.Contains("Luna: A smart dog bowl.", StringComparison.Ordinal));
        Assert.Equal(2, discussion.Turns.Max(turn => turn.Round));
        Assert.True(discussion.Agreed);
    }

    [Fact]
    public async Task TheGroupChatStopsAtTheRoundLimitWithoutAgreement()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartAsync(ct);
        var (app, scope) = await Install(brain, "group-chat");
        await brain.Get<IScriptedLLM>(scope + "/luna").Script(["A leash.", "A collar.", "A leash with a collar."]);
        await brain.Get<IScriptedLLM>(scope + "/gemma").Script(["Not a leash.", "Not a collar."]);
        await app.Configure(new ConfigureApp(Guid.NewGuid(), new Dictionary<string, string>
        {
            ["LunaModel"] = IScriptedLLM.ModelPrefix + scope + "/luna",
            ["GemmaModel"] = IScriptedLLM.ModelPrefix + scope + "/gemma",
            ["MaxRounds"] = "2",
        }));

        var (answer, invocationId) = await Ask(app, "Pick one accessory", ct);

        Assert.Equal("A leash with a collar.", answer);
        var discussion = await brain.Get<IGroupChat>(GroupChatRuntime.ChatKey(scope + "/app", invocationId)).Read();
        Assert.Equal(2, discussion.Turns.Max(turn => turn.Round));
        Assert.False(discussion.Agreed);
    }

    private static async Task<(IApp App, string Scope)> Install(UnitBrain brain, string name)
    {
        var shipped = Source.Load().Single(app => app.Package.Name == name);
        StampPublisher();
        var revision = await brain.Get<IPackage>(shipped.Package.ToString()).Commit(new CommitPackage(Guid.NewGuid(), null, shipped.Content, "Ship"));
        var scope = $"scratch/{name}/{Guid.NewGuid():N}";
        var app = brain.Get<IApp>(scope + "/app");
        await app.Install(new InstallApp(Guid.NewGuid(), new(shipped.Package, revision.Id), new Dictionary<string, string>()));
        return (app, scope);
    }

    private static async Task<(string Answer, Guid InvocationId)> Ask(IApp app, string input, CancellationToken ct)
    {
        var invocation = await app.Invoke(new InvokeApp(Guid.NewGuid(), "ask", input));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(60));
        while (invocation.Status == InvocationStatus.Pending)
        {
            await Task.Delay(100, deadline.Token);
            invocation = await app.ReadInvocation(invocation.Id);
        }
        Assert.True(invocation.Status == InvocationStatus.Completed, invocation.Error ?? "The app failed.");
        return (invocation.Output ?? "", invocation.Id);
    }

    private static void StampPublisher() => CallerContextStamper.Stamp(new CallerContext
    {
        PrincipalId = Source.Publisher,
        AccountId = Source.Publisher,
        BrainId = Source.Publisher,
        Kind = CallerKind.Platform,
        StampedBy = TrustedEdge.Platform,
    });

    private static Task<UnitBrain> StartAsync(CancellationToken ct) => UnitTest.Create()
        .WithModule<AIModule>()
        .WithModule<AppsModule>()
        .ConfigureSilo(silo =>
        {
            silo.Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        })
        .StartAsync(ct);
}


