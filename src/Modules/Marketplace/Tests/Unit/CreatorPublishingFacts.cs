using DigitalBrain.Apps;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Core.Enforcement;
using DigitalBrain.Marketplace.Creators;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Modules.Marketplace.Tests.Unit;

public sealed class CreatorPublishingFacts
{
    private const string Marketplace = "marketplace-publishing";

    [Fact]
    public async Task ConsumerAndPrepaidFlagsDefaultOff()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<CreatorPublishingModule>().StartAsync(ct);
        var publishing = brain.Get<ICreatorPublishing>(Marketplace);

        var features = await publishing.Features();

        Assert.False(features.SelfServeSignUp);
        Assert.False(features.PrepaidCompute);
        Assert.False(features.ConsumerTerms);
    }

    [Fact]
    public async Task ASavedDeclarativeAppPublishesAfterCertificationOnFakes()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<CreatorPublishingModule>().StartAsync(ct);
        var publishing = brain.Get<ICreatorPublishing>(Marketplace);

        var result = await publishing.Publish(Publish(Manifest("1.0.0"), "owner"));

        Assert.Equal(CreatorPublishOutcome.Published, result.Outcome);
        Assert.NotNull(result.Listing);
        Assert.Equal(PublishingRing.CreatorDeclarative, result.Listing!.Ring);
        Assert.Equal(CreatorListingState.Published, result.Listing.State);
        Assert.True(result.Listing.Beta);
        Assert.True(result.Certification!.Certified);
        Assert.All(result.Certification.Scenarios, evidence => Assert.True(evidence.Passed));
        Assert.Equal("1.0.0", (await publishing.Read(Manifest("1.0.0").Id))!.Version);
    }

    [Fact]
    public async Task PermissionGrowthTriggersReConsentAndPublishesWithTheFingerprint()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<CreatorPublishingModule>().StartAsync(ct);
        var publishing = brain.Get<ICreatorPublishing>(Marketplace);

        await publishing.Publish(Publish(Manifest("1.0.0"), "owner"));
        var grown = Manifest("1.0.1") with
        {
            Permissions = [new AppPermission { SemanticTypeId = "email", Reason = "Greet the customer by email." }],
        };

        var blocked = await publishing.Publish(Publish(grown, "owner"));
        Assert.Equal(CreatorPublishOutcome.ReConsentRequired, blocked.Outcome);
        Assert.NotNull(blocked.ReConsent);
        Assert.Contains("email", blocked.ReConsent!.AddedPermissions);
        Assert.Equal("1.0.0", (await publishing.Read("test.saved-app"))!.Version);

        var consented = await publishing.Publish(Publish(grown, "owner") with { ReConsentFingerprint = blocked.ReConsent.Fingerprint });
        Assert.Equal(CreatorPublishOutcome.Published, consented.Outcome);
        Assert.Equal("1.0.1", consented.Listing!.Version);
    }

    [Fact]
    public async Task PriceGrowthTriggersReConsent()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<CreatorPublishingModule>().StartAsync(ct);
        var publishing = brain.Get<ICreatorPublishing>(Marketplace);

        await publishing.Publish(Publish(Manifest("1.0.0"), "owner"));
        var priced = Manifest("1.0.1") with
        {
            Meters = [new AppMeter { MeterId = "search.request", Unit = "call", Aggregation = "count", ProposedPriceInCompute = 5m }],
        };

        var blocked = await publishing.Publish(Publish(priced, "owner"));

        Assert.Equal(CreatorPublishOutcome.ReConsentRequired, blocked.Outcome);
        Assert.Equal("search.request", Assert.Single(blocked.ReConsent!.PriceIncreases).MeterId);
    }

    [Fact]
    public async Task RemoteAppIsRejectedBecauseCodeNeverRunsInTheSilo()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<CreatorPublishingModule>().StartAsync(ct);
        var publishing = brain.Get<ICreatorPublishing>(Marketplace);

        var remote = Manifest("1.0.0") with { Kind = AppKind.Remote, RemoteEndpoint = "https://example.test/mcp" };
        var result = await publishing.Publish(Publish(remote, "owner"));

        Assert.Equal(CreatorPublishOutcome.RejectedNotDeclarative, result.Outcome);
        Assert.Empty(await publishing.List());
    }

    [Fact]
    public async Task KillSwitchBlocksPublishingAndMarksTheListingKilled()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<CreatorPublishingModule>().StartAsync(ct);
        var publishing = brain.Get<ICreatorPublishing>(Marketplace);
        await publishing.Publish(Publish(Manifest("1.0.0"), "owner"));

        var killed = await publishing.Kill("test.saved-app", "Removed for abuse.");
        Assert.Equal(CreatorListingState.Killed, killed.State);

        var blocked = await publishing.Publish(Publish(Manifest("1.0.1"), "owner"));
        Assert.Equal(CreatorPublishOutcome.KillSwitchActive, blocked.Outcome);

        await publishing.Restore("test.saved-app");
        var restored = await publishing.Publish(Publish(Manifest("1.0.1"), "owner"));
        Assert.Equal(CreatorPublishOutcome.Published, restored.Outcome);
    }

    [Fact]
    public async Task OnlyTheCreatorMayPublishANewVersion()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<CreatorPublishingModule>().StartAsync(ct);
        var publishing = brain.Get<ICreatorPublishing>(Marketplace);
        await publishing.Publish(Publish(Manifest("1.0.0"), "owner"));

        var denied = await publishing.Publish(Publish(Manifest("1.0.1"), "someone-else"));

        Assert.Equal(CreatorPublishOutcome.IdentityDenied, denied.Outcome);
    }

    [Fact]
    public async Task TheCallFilterCanStopPublishing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<CreatorPublishingModule>()
            .ConfigureSilo(silo => silo.Services.AddSingleton<ICallFilterStage, DenyPublishStage>())
            .StartAsync(ct);
        var publishing = brain.Get<ICreatorPublishing>(Marketplace);

        var result = await publishing.Publish(Publish(Manifest("1.0.0"), "owner"));

        Assert.Equal(CreatorPublishOutcome.CallFilterDenied, result.Outcome);
    }

    [Fact]
    public async Task AnAppWithoutScenariosIsNotCertified()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<CreatorPublishingModule>().StartAsync(ct);
        var publishing = brain.Get<ICreatorPublishing>(Marketplace);

        var result = await publishing.Publish(Publish(Manifest("1.0.0") with { Scenarios = [] }, "owner"));

        Assert.Equal(CreatorPublishOutcome.NotCertified, result.Outcome);
        Assert.False(result.Certification!.Certified);
    }

    private static PublishDeclarativeRequest Publish(AppManifest manifest, string principal) => new()
    {
        Manifest = manifest,
        Caller = new CallerContext
        {
            PrincipalId = principal,
            AccountId = "account",
            WorkspaceId = "workspace",
            Kind = CallerKind.User,
            StampedBy = TrustedEdge.AuthenticatedHttp,
        },
    };

    private static AppManifest Manifest(string version) => new()
    {
        Id = "test.saved-app",
        Version = version,
        Publisher = "workspace",
        Kind = AppKind.Declarative,
        Name = "Saved app",
        DescriptionForPeople = "A saved declarative app.",
        DescriptionForModel = "Read and submit the saved app.",
        Operations =
        [
            new AppOperation
            {
                Name = "Submit",
                DescriptionForModel = "Submit the saved app.",
                InputTypeIds = new Dictionary<string, string> { ["note"] = "plain-text" },
            },
        ],
        Scenarios =
        [
            new AppScenario
            {
                Name = "Submit a note",
                Given = "a saved app",
                When = "I submit the form",
                Then = "the note is stored",
            },
        ],
    };

    private sealed class DenyPublishStage : ICallFilterStage
    {
        public ValueTask<CallDecision?> EvaluateAsync(CallRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<CallDecision?>(CallDecision.Deny(CallDenial.MissingAllowance, "No allowance for publishing."));
    }
}
