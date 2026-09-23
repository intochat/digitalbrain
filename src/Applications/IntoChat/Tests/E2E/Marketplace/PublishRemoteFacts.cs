using DigitalBrain.Apps;
using DigitalBrain.Broker;
using DigitalBrain.Compute;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Marketplace;
using DigitalBrain.Receipts;
using DigitalBrain.Testing.Unit;
using Microsoft.Extensions.DependencyInjection;

namespace IntoChat.Tests.E2E.Marketplace;

// J6: an invited remote developer submits a remote app. It is certified on fakes, listed as Beta,
// proposed for an intent, consented to, run through the broker, and recorded on a receipt.
public sealed class PublishRemoteFacts
{
    [Fact(Timeout = 240_000)]
    public async Task AnInvitedRemoteAppIsCertifiedListedProposedConsentedRunAndReceipted()
    {
        var ct = TestContext.Current.CancellationToken;
        var transport = new HonestRemoteApp();
        await using var brain = await UnitTest.Create()
            .WithModule<AppsModule>()
            .WithModule<BrokerModule>()
            .WithModule<ComputeModule>()
            .WithModule<ReceiptsModule>()
            .WithModule<MarketplaceModule>()
            .ConfigureSilo(silo => silo.Services.AddSingleton<IRemoteAppTransport>(transport))
            .StartAsync(ct);

        var catalog = brain.Get<IListingCatalog>("catalog");
        var publisher = Publisher();
        var manifest = RemoteManifest();

        // 1. certify on fakes: namespace proof, Ed25519 signature, scans, scenarios, diff, golden prompts.
        var challenge = new DnsTxtNamespaceProofAuthority().IssueChallenge(publisher.Namespace, publisher.Domain);
        DnsTxtNamespaceProofAuthority.Reset();
        DnsTxtNamespaceProofAuthority.PublishRecord(challenge.RecordName, challenge.Token);
        var (publicKey, seed) = Ed25519Keys.Generate();
        var signature = new AppSignature
        {
            KeyId = "acme-1",
            PublicKey = publicKey,
            Signature = Ed25519Keys.Sign(seed, Ed25519Keys.Payload(manifest)),
        };

        var evidence = await catalog.CertifyAsync(manifest, publisher, signature, artifact: null, ct);
        Assert.Equal(CertificationOutcome.Certified, evidence.Outcome);
        Assert.True(evidence.NamespaceProof.Verified);
        Assert.Equal(SignatureOutcome.Valid, evidence.Signature);
        Assert.All(evidence.Scenarios, scenario => Assert.True(scenario.Passed));
        Assert.True(evidence.Diff.IsClean);
        Assert.True(evidence.GoldenPrompts.Precision >= 1d);

        // 2. list as Beta and propose it for a matching intent with published ranking parameters.
        var listing = await catalog.PublishAsync(manifest, publisher, evidence);
        Assert.Equal(ListingStatus.Beta, listing.Status);
        Assert.True(listing.Beta);
        Assert.True(listing.Certification.Passed);

        var ranked = await catalog.RankAsync("search the acme catalog");
        Assert.Contains(ranked, candidate => candidate.Manifest.Id == manifest.Id);
        await catalog.AcceptIntentAsync(manifest.Id, "search the acme catalog");
        Assert.Equal(1, (await catalog.ReadAsync(manifest.Id))!.AcceptedIntents);
        Assert.Contains("accepted-intents", (await catalog.ReadRankingParametersAsync()).Signals);

        // 3. install consent: the buyer approves the app up to a monthly limit.
        var ledger = brain.Get<IAllowanceLedger>("acme-account");
        await ledger.GrantAsync(new Allowance
        {
            AllowanceId = $"install-{manifest.Id}",
            AccountId = "acme-account",
            WorkspaceId = "ws-buyer",
            AppId = manifest.Id,
            Level = ApprovalLevel.InstallConsent,
            Scope = AllowanceScope.Always,
            LimitCompute = 50m,
            MonthlyLimitCompute = 50m,
            Permissions = [.. manifest.Permissions.Select(permission => permission.SemanticTypeId)],
            PriceBookVersion = new PriceBook().Version,
            GrantedAt = DateTimeOffset.UtcNow,
        }, ct);

        // 4. run through the broker: the only path a third-party call may take.
        var gateway = brain.SiloServices.GetRequiredService<IRemoteAppGateway>();
        var result = await gateway.InvokeAsync(new RemoteCallRequest
        {
            Manifest = manifest,
            Caller = new CallerContext
            {
                PrincipalId = "buyer",
                AccountId = "acme-account",
                WorkspaceId = "ws-buyer",
                Kind = CallerKind.Assistant,
                StampedBy = TrustedEdge.AuthenticatedHttp,
                IntentId = "j6-intent",
                ConversationId = "chat-j6",
            },
            Operation = "Search",
            DataClasses = ["search.query"],
            EgressHosts = ["acme.example"],
            IntentId = "j6-intent",
            EstimatedCompute = 5m,
        }, ct);

        Assert.True(result.Allowed, result.Explanation);
        Assert.True(transport.Invoked);
        Assert.NotNull(result.Diff);
        Assert.True(result.Diff!.IsClean);

        // 5. the workspace ran the app, so it may review it.
        await catalog.RecordUsageAsync(manifest.Id, "ws-buyer");
        var review = await catalog.ReviewAsync(manifest.Id, "ws-buyer", 5, "Ran first try.");
        Assert.Equal(5, review.Stars);

        // 6. the intent gets a durable receipt naming the app call.
        var receipt = brain.Get<IReceipt>("j6-intent");
        await receipt.Write(new ReceiptDraft
        {
            WorkspaceId = "ws-buyer",
            ConversationId = "chat-j6",
            Outcome = ReceiptOutcome.Succeeded,
            Summary = "Ran the certified remote app through the broker.",
            Calls = [new AppCall { AppId = manifest.Id, Operation = "Search", Succeeded = true }],
            EstimatedCompute = 5m,
            ApprovedComputeLimit = 50m,
            ActualCompute = 5m,
            ShadowPriced = false,
        });

        var written = await receipt.Read();
        Assert.NotNull(written);
        Assert.Equal(ReceiptOutcome.Succeeded, written!.Content.Outcome);
        Assert.Contains(written.Content.Calls, call => call.AppId == manifest.Id && call.Succeeded);
    }

    private static PublisherProfile Publisher() => new()
    {
        PublisherId = "acme",
        Namespace = "com.acme",
        Domain = "acme.example",
        Ring = PublishRing.InvitedRemote,
        Invited = true,
        RegisteredAt = DateTimeOffset.UtcNow,
    };

    private static AppManifest RemoteManifest() => new()
    {
        Id = "com.acme.search",
        Version = "1.0.0",
        Publisher = "acme",
        Kind = AppKind.Remote,
        Name = "Acme Search",
        DescriptionForPeople = "Search a company catalog.",
        DescriptionForModel = "Searches the Acme catalog.",
        RemoteEndpoint = "https://acme.example/mcp",
        Permissions = [new AppPermission { SemanticTypeId = "search.query", Reason = "read queries" }],
        Meters = [new AppMeter { MeterId = "search.request", Unit = "request", Aggregation = "sum" }],
        Operations = [new AppOperation { Name = "Search", DescriptionForModel = "Search the Acme catalog." }],
        ExamplePrompts = ["Search the Acme catalog"],
        Scenarios = [new AppScenario { Name = "search", Given = "a query", When = "Search runs", Then = "results return" }],
    };

    private sealed class HonestRemoteApp : IRemoteAppTransport
    {
        public bool Invoked { get; private set; }

        public ValueTask<RemoteAppResponse> InvokeAsync(
            AppManifest manifest, RemoteCallRequest request, CancellationToken cancellationToken = default)
        {
            Invoked = true;
            return ValueTask.FromResult(new RemoteAppResponse
            {
                Succeeded = true,
                Output = "3 results",
                UsedDataClasses = [.. manifest.Permissions.Select(permission => permission.SemanticTypeId)],
                UsedMeters = [.. manifest.Meters.Select(meter => meter.MeterId)],
            });
        }
    }
}
