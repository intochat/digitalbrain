using DigitalBrain.Apps;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Marketplace.Creators;
using DigitalBrain.Testing.Unit;

namespace IntoChat.Tests.E2E.Marketplace;

// A regular user publishes a saved declarative app in one step, and a later version that widens
// permissions is refused until the publisher re-consents. No code runs in the silo.
public sealed class PublishDeclarativeFacts
{
    [Fact(Timeout = 120_000)]
    public async Task ASavedAppPublishesAndAPermissionGrowthTriggersReConsent()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create()
            .WithModule<AppsModule>()
            .WithModule<CreatorPublishingModule>()
            .StartAsync(ct);

        var catalog = brain.Get<IAppCatalog>("workspace-a");
        var publishing = brain.Get<ICreatorPublishing>("marketplace-publishing");

        var saved = await catalog.SaveAsApp(SavedApp(withEmailPermission: false));
        var published = await publishing.Publish(Publish(saved.Manifest));

        Assert.Equal(CreatorPublishOutcome.Published, published.Outcome);
        Assert.Equal(PublishingRing.CreatorDeclarative, published.Listing!.Ring);
        Assert.True(published.Listing.Beta);
        Assert.True(published.Certification!.Certified);
        Assert.False((await publishing.Features()).SelfServeSignUp);

        var grown = await catalog.SaveAsApp(SavedApp(withEmailPermission: true));
        var blocked = await publishing.Publish(Publish(grown.Manifest));

        Assert.Equal(CreatorPublishOutcome.ReConsentRequired, blocked.Outcome);
        Assert.Contains("email", blocked.ReConsent!.AddedPermissions);
        Assert.Equal("1.0.0", (await publishing.Read(saved.Manifest.Id))!.Version);

        var consented = await publishing.Publish(Publish(grown.Manifest) with { ReConsentFingerprint = blocked.ReConsent.Fingerprint });
        Assert.Equal(CreatorPublishOutcome.Published, consented.Outcome);
        Assert.Equal("1.0.1", consented.Listing!.Version);
    }

    private static PublishDeclarativeRequest Publish(AppManifest manifest) => new()
    {
        Manifest = manifest,
        Caller = new CallerContext
        {
            PrincipalId = "creator",
            AccountId = "account",
            WorkspaceId = "workspace-a",
            Kind = CallerKind.User,
            StampedBy = TrustedEdge.AuthenticatedHttp,
        },
    };

    private static SaveAsAppRequest SavedApp(bool withEmailPermission) => new()
    {
        Id = "intochat.saved-intake",
        Name = "Customer intake",
        DescriptionForPeople = "Take a new customer in one form.",
        DescriptionForModel = "Show the intake form and submit it atomically.",
        UiEntry = "app-saved-intake",
        Operations =
        [
            new AppOperation
            {
                Name = "Submit",
                DescriptionForModel = "Submit the intake form.",
                InputTypeIds = new Dictionary<string, string> { ["name"] = "plain-text", ["email"] = "email" },
                OutputTypeId = "reference",
            },
        ],
        ExamplePrompts = ["Add a new customer"],
        Scenarios =
        [
            new AppScenario
            {
                Name = "Submit the intake form",
                Given = "a customer window",
                When = "I submit the form",
                Then = "a customer is created",
            },
        ],
        Permissions = withEmailPermission
            ? [new AppPermission { SemanticTypeId = "email", Reason = "Email the customer a receipt." }]
            : [],
    };
}
