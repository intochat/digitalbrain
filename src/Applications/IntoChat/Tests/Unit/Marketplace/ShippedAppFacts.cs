using DigitalBrain.AI;
using DigitalBrain.Apps;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Core.Enforcement;
using DigitalBrain.Specs;
using IntoChat.Marketplace;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IntoChat.Tests.Unit.Marketplace;

// Every shipped app's spec binds to the brain's steps, and its deterministic scenarios pass against
// the real runtimes. The @live scenarios need real models and run when the app is verified in the product.
public sealed class ShippedAppFacts
{
    private static readonly string[] LiveTags = ["@live"];

    public static TheoryData<string> Apps() => [.. ShippedApps.Load().Select(app => app.Package.Name)];

    [Fact]
    public void GroupChatAssistantAndWordCountShip()
        => Assert.Equal(["assistant", "group-chat", "word-count"], ShippedApps.Load().Select(app => app.Package.Name).Order());

    [Theory]
    [MemberData(nameof(Apps))]
    public async Task EveryStepOfTheSpecIsOneTheBrainUnderstands(string name)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartAsync(ct);
        var spec = Shipped(name).Content.File(PackageContent.SpecPath)!;

        var feature = await brain.Get<IFeature>("shipped/" + name).Set(spec);

        Assert.Null(feature.Problem);
        var unbound = feature.Scenarios.SelectMany(scenario => scenario.Steps).Where(step => !step.Bound).Select(step => $"line {step.Line}: {step.Text}");
        Assert.Empty(unbound);
    }

    [Theory]
    [InlineData("group-chat")]
    [InlineData("assistant")]
    public async Task DeterministicScenariosPassAgainstTheRealRuntime(string name)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartAsync(ct);
        var app = Shipped(name);
        StampPublisher();
        var revision = await brain.Get<IPackage>(app.Package.ToString()).Commit(new CommitPackage(Guid.NewGuid(), null, app.Content, "Ship"));
        var feature = brain.Get<IFeature>("shipped/" + name);
        var bound = await feature.Set(app.Content.File(PackageContent.SpecPath)!);
        foreach (var scenario in bound.Scenarios)
        { await brain.Get<IApp>($"scratch/{name}/{scenario.Line}").Install(new InstallApp(Guid.NewGuid(), new(app.Package, revision.Id), new Dictionary<string, string>())); }

        var run = await feature.Run($"scratch/{name}/{FeatureSnapshot.ScenarioPlaceholder}", LiveTags);

        var ran = run.Scenarios.Where(scenario => scenario.Verdict != Verdict.Skipped).ToArray();
        Assert.NotEmpty(ran);
        Assert.All(ran, scenario => Assert.True(scenario.Verdict == Verdict.Passed,
            $"{scenario.Name}: {string.Join("; ", scenario.Steps.Where(step => step.Message is not null).Select(step => $"line {step.Line} {step.Message}"))}"));
    }

    private static ShippedApp Shipped(string name) => ShippedApps.Load().Single(app => app.Package.Name == name);

    private static void StampPublisher() => CallerContextStamper.Stamp(new CallerContext
    {
        PrincipalId = ShippedApps.Publisher,
        AccountId = ShippedApps.Publisher,
        WorkspaceId = ShippedApps.Publisher,
        Kind = CallerKind.Platform,
        StampedBy = TrustedEdge.Platform,
    });

    private static Task<UnitBrain> StartAsync(CancellationToken ct) => UnitTest.Create()
        .WithModule<AIModule>()
        .WithModule<SpecsModule>()
        .WithModule<AppsModule>()
        .ConfigureSilo(silo =>
        {
            silo.Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
            silo.Services.AddAppRuntime<GroupChatRuntime>();
            silo.Services.AddAppRuntime<PromptRuntime>();
            silo.Services.AddSingleton<StepLibrary, ModelSteps>();
            silo.Services.AddSingleton<StepLibrary, GroupChatSteps>();
        })
        .StartAsync(ct);
}
