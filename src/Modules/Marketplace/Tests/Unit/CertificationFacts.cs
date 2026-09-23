using DigitalBrain.Apps;
using DigitalBrain.Broker;
using DigitalBrain.Marketplace;
using DigitalBrain.Testing.Unit;
using Xunit;

namespace DigitalBrain.Modules.Marketplace.Tests.Unit;

public sealed class CertificationFacts
{
    [Fact]
    public async Task CertificationEvidenceIsAttachedToEveryListing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<MarketplaceModule>().StartAsync(ct);
        var catalog = brain.Get<IListingCatalog>("catalog");
        var manifest = RemoteManifest();

        var evidence = await Certified(manifest);
        var listing = await catalog.PublishAsync(manifest, Publisher(), evidence);

        Assert.True(listing.Certification.Passed);
        Assert.Equal(CertificationOutcome.Certified, listing.Certification.Outcome);
        Assert.Equal(ListingStatus.Beta, listing.Status);
        Assert.True(listing.Beta);
        Assert.Equal(manifest.Id, listing.Certification.AppId);

        var reread = await catalog.ReadAsync(manifest.Id);
        Assert.NotNull(reread);
        Assert.NotNull(reread!.Certification);
        Assert.True(reread.Certification.NamespaceProof.Verified);
        Assert.Equal(SignatureOutcome.Valid, reread.Certification.Signature);
    }

    [Fact]
    public async Task AnUnsignedRemoteAppIsRejectedInRequireMode()
    {
        var request = new CertificationRequest { Manifest = RemoteManifest(), Publisher = Publisher(), Signature = null };
        var evidence = await Service(signature: SignatureOutcome.Missing).CertifyAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(CertificationOutcome.Rejected, evidence.Outcome);
        Assert.Contains(evidence.Failures, failure => failure.Contains("Signature", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ARealEd25519SignatureVerifiesAndTamperingIsRejected()
    {
        var manifest = RemoteManifest();
        var (publicKey, seed) = Ed25519Keys.Generate();
        var signature = new AppSignature
        {
            KeyId = "acme-1",
            PublicKey = publicKey,
            Signature = Ed25519Keys.Sign(seed, Ed25519Keys.Payload(manifest)),
        };
        var verifier = new Ed25519AppSignatureVerifier();

        Assert.Equal(SignatureOutcome.Valid, verifier.Verify(manifest, signature));
        Assert.Equal(SignatureOutcome.Invalid, verifier.Verify(manifest with { Name = "Tampered" }, signature));
        Assert.Equal(SignatureOutcome.Missing, verifier.Verify(manifest, null));
    }

    [Fact]
    public async Task AProofThatWasNeverPublishedToTheNamespaceIsRejected()
    {
        var request = new CertificationRequest { Manifest = RemoteManifest(), Publisher = Publisher() };
        var evidence = await Service(namespaceVerified: false).CertifyAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(CertificationOutcome.Rejected, evidence.Outcome);
        Assert.False(evidence.NamespaceProof.Verified);
        Assert.Contains(evidence.Failures, failure => failure.Contains("Namespace proof", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ARiskyDataClassQueuesAHumanReview()
    {
        var manifest = RemoteManifest(riskyEmail: true);
        var request = new CertificationRequest { Manifest = manifest, Publisher = Publisher() };
        var evidence = await Service().CertifyAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(CertificationOutcome.PendingHumanReview, evidence.Outcome);
        Assert.Contains("person.email", evidence.RiskyDataClasses);
        Assert.False(evidence.Passed);
    }

    [Fact]
    public async Task AnUndeclaredDataClassFailsTheDeclaredVersusUsedDiff()
    {
        var ct = TestContext.Current.CancellationToken;
        var manifest = RemoteManifest();
        var dirty = new DeclaredObservedDiff
        {
            AppId = manifest.Id,
            UndeclaredDataClasses = ["person.email"],
        };
        var evidence = await Service(diff: dirty).CertifyAsync(
            new CertificationRequest { Manifest = manifest, Publisher = Publisher() }, ct);

        Assert.Equal(CertificationOutcome.Rejected, evidence.Outcome);
        Assert.False(evidence.Diff.IsClean);
        Assert.Contains(evidence.Failures, failure => failure.Contains("diff", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task UncertifiedEvidenceCannotBeListed()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<MarketplaceModule>().StartAsync(ct);
        var catalog = brain.Get<IListingCatalog>("catalog");
        var manifest = RemoteManifest();
        var rejected = await Service(namespaceVerified: false).CertifyAsync(
            new CertificationRequest { Manifest = manifest, Publisher = Publisher() }, ct);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => catalog.PublishAsync(manifest, Publisher(), rejected));
    }

    [Fact]
    public async Task TheKillSwitchDisablesTheAppWithAStatementOfReasons()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<MarketplaceModule>().StartAsync(ct);
        var catalog = brain.Get<IListingCatalog>("catalog");
        var manifest = RemoteManifest();
        await catalog.PublishAsync(manifest, Publisher(), await Certified(manifest));

        var disabled = await catalog.DisableAsync(manifest.Id, "Trademark complaint upheld.");

        Assert.Equal(ListingStatus.Disabled, disabled.Status);
        Assert.Equal("Trademark complaint upheld.", disabled.DisabledReason);
        Assert.Equal(ListingStatus.Disabled, (await catalog.ReadAsync(manifest.Id))!.Status);
    }

    [Fact]
    public async Task ReviewsComeOnlyFromWorkspacesThatRanTheApp()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<MarketplaceModule>().StartAsync(ct);
        var catalog = brain.Get<IListingCatalog>("catalog");
        var manifest = RemoteManifest();
        await catalog.PublishAsync(manifest, Publisher(), await Certified(manifest));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => catalog.ReviewAsync(manifest.Id, "ws-stranger", 5, "never ran it"));

        await catalog.RecordUsageAsync(manifest.Id, "ws-user");
        var review = await catalog.ReviewAsync(manifest.Id, "ws-user", 5, "Worked first try.");

        Assert.Equal(5, review.Stars);
        Assert.Single(await catalog.ReadReviewsAsync(manifest.Id));
    }

    [Fact]
    public async Task RankingServesAnAcceptedIntentAndPublishesItsParameters()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<MarketplaceModule>().StartAsync(ct);
        var catalog = brain.Get<IListingCatalog>("catalog");
        var manifest = RemoteManifest();
        await catalog.PublishAsync(manifest, Publisher(), await Certified(manifest));

        var ranked = await catalog.RankAsync("search the acme catalog");
        Assert.Contains(ranked, listing => listing.Manifest.Id == manifest.Id);

        await catalog.AcceptIntentAsync(manifest.Id, "search the acme catalog");
        Assert.Equal(1, (await catalog.ReadAsync(manifest.Id))!.AcceptedIntents);

        var parameters = await catalog.ReadRankingParametersAsync();
        Assert.Contains("accepted-intents", parameters.Signals);
        Assert.False(string.IsNullOrWhiteSpace(parameters.Formula));
    }

    private static async Task<CertificationEvidence> Certified(AppManifest manifest)
    {
        var (publicKey, seed) = Ed25519Keys.Generate();
        var request = new CertificationRequest
        {
            Manifest = manifest,
            Publisher = Publisher(),
            Signature = new AppSignature
            {
                KeyId = "acme-1",
                PublicKey = publicKey,
                Signature = Ed25519Keys.Sign(seed, Ed25519Keys.Payload(manifest)),
            },
        };
        var evidence = await new CertificationService(
            new FakeNamespaceProof(true),
            new Ed25519AppSignatureVerifier(),
            new FakeScanner([]),
            new FakeScenarioRunner([]),
            new FakeGolden(new GoldenPromptReport { Total = 3, Accepted = 3 }),
            new FakeObservations(null)).CertifyAsync(request);
        Assert.Equal(CertificationOutcome.Certified, evidence.Outcome);
        return evidence;
    }

    private static CertificationService Service(
        bool namespaceVerified = true,
        SignatureOutcome signature = SignatureOutcome.Valid,
        IReadOnlyList<ScanFinding>? findings = null,
        IReadOnlyList<ScenarioResult>? scenarios = null,
        DeclaredObservedDiff? diff = null,
        GoldenPromptReport? golden = null)
        => new(
            new FakeNamespaceProof(namespaceVerified),
            new FakeSignatureVerifier(signature),
            new FakeScanner(findings ?? []),
            new FakeScenarioRunner(scenarios ?? []),
            new FakeGolden(golden ?? new GoldenPromptReport { Total = 3, Accepted = 3 }),
            new FakeObservations(diff));

    private static PublisherProfile Publisher() => new()
    {
        PublisherId = "acme",
        Namespace = "com.acme",
        Domain = "acme.example",
        Ring = PublishRing.InvitedRemote,
        Invited = true,
        RegisteredAt = DateTimeOffset.UnixEpoch,
    };

    private static AppManifest RemoteManifest(bool riskyEmail = false) => new()
    {
        Id = "com.acme.search",
        Version = "1.0.0",
        Publisher = "acme",
        Kind = AppKind.Remote,
        Name = "Acme Search",
        DescriptionForPeople = "Search a company catalog.",
        DescriptionForModel = "Searches the Acme catalog.",
        RemoteEndpoint = "https://acme.example/mcp",
        Permissions = riskyEmail
            ? [new AppPermission { SemanticTypeId = "search.query", Reason = "read queries" },
               new AppPermission { SemanticTypeId = "person.email", Reason = "contact results" }]
            : [new AppPermission { SemanticTypeId = "search.query", Reason = "read queries" }],
        Meters = [new AppMeter { MeterId = "search.request", Unit = "request", Aggregation = "sum" }],
        Operations = [new AppOperation { Name = "Search", DescriptionForModel = "Search the Acme catalog." }],
        ExamplePrompts = ["Search the Acme catalog"],
        Scenarios = [new AppScenario { Name = "search", Given = "a query", When = "Search runs", Then = "results return" }],
    };

    private sealed class FakeNamespaceProof(bool verified) : INamespaceProofAuthority
    {
        public NamespaceProofChallenge IssueChallenge(string publisherNamespace, string domain) => new()
        {
            Namespace = publisherNamespace,
            Domain = domain,
            RecordName = DnsTxtNamespaceProofAuthority.RecordPrefix + domain,
            Token = "token",
        };

        public NamespaceProofEvidence Verify(NamespaceProofChallenge challenge) => new()
        {
            Namespace = challenge.Namespace,
            Outcome = verified ? NamespaceProofOutcome.Verified : NamespaceProofOutcome.RecordMissing,
            CheckedAt = DateTimeOffset.UnixEpoch,
        };
    }

    private sealed class FakeSignatureVerifier(SignatureOutcome outcome) : IAppSignatureVerifier
    {
        public SignatureOutcome Verify(AppManifest manifest, AppSignature? signature) => outcome;
    }

    private sealed class FakeScanner(IReadOnlyList<ScanFinding> findings) : IManifestScanner
    {
        public IReadOnlyList<ScanFinding> Scan(AppManifest manifest, byte[]? artifact) => findings;
    }

    private sealed class FakeScenarioRunner(IReadOnlyList<ScenarioResult> results) : IManifestScenarioRunner
    {
        public Task<IReadOnlyList<ScenarioResult>> RunAsync(AppManifest manifest, CancellationToken cancellationToken = default)
            => Task.FromResult(results);
    }

    private sealed class FakeGolden(GoldenPromptReport report) : IGoldenPromptEvaluator
    {
        public Task<GoldenPromptReport> EvaluateAsync(AppManifest manifest, CancellationToken cancellationToken = default)
            => Task.FromResult(report);
    }

    private sealed class FakeObservations(DeclaredObservedDiff? diff) : IAppObservationStore
    {
        public void Record(string appId, IEnumerable<string> dataClasses, IEnumerable<string> meters)
        {
        }

        public AppObservation Observe(string appId) => new() { AppId = appId };

        public DeclaredObservedDiff Diff(AppManifest manifest) =>
            diff ?? DeclaredObservedDiff.Compute(manifest, new AppObservation { AppId = manifest.Id });
    }
}
