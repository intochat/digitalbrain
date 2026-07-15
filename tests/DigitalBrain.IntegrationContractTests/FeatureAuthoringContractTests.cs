using System.Reflection;
using System.Text.Json;
using DigitalBrain.Kernel.Capabilities;
using DigitalBrain.Kernel.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Serialization;
using Xunit;

namespace DigitalBrain.IntegrationContractTests;

public sealed class FeatureAuthoringContractTests
{
    [Fact]
    public void Authoring_contract_aliases_fields_and_constructor_shapes_are_exact()
    {
        AssertShape<FeatureDraftPatch>("digitalbrain.feature.draft-patch.v1",
            (0, "PatchId", typeof(string)),
            (1, "DraftId", typeof(FeatureDraftId)),
            (2, "BaseRevision", typeof(long)),
            (3, "Summary", typeof(string)),
            (4, "ReplacementBehavior", typeof(FeatureBehavior)),
            (5, "ReplacementSource", typeof(FeatureSourceSnapshot)));
        AssertShape<SuggestFeatureChange>("digitalbrain.feature.suggest-change.v1",
            (0, "DraftId", typeof(FeatureDraftId)),
            (1, "ExpectedRevision", typeof(long)),
            (2, "Guidance", typeof(string)),
            (3, "SuggestionId", typeof(string)));
        AssertShape<AcceptSuggestedChange>("digitalbrain.feature.accept-suggested-change.v1",
            (0, "Patch", typeof(FeatureDraftPatch)),
            (1, "ExpectedRevision", typeof(long)),
            (2, "IdempotencyId", typeof(string)),
            (3, "AcceptedAt", typeof(DateTimeOffset)));
        AssertShape<RejectSuggestedChange>("digitalbrain.feature.reject-suggested-change.v1",
            (0, "DraftId", typeof(FeatureDraftId)),
            (1, "PatchId", typeof(string)),
            (2, "BaseRevision", typeof(long)),
            (3, "ExpectedRevision", typeof(long)));
        AssertShape<VerifyFeatureDraft>("digitalbrain.feature.verify-draft.v1",
            (0, "DraftId", typeof(FeatureDraftId)),
            (1, "ExpectedRevision", typeof(long)),
            (2, "IdempotencyId", typeof(string)));
        Assert.Equal(
            "digitalbrain.feature.scenario-outcome.v1",
            typeof(FeatureScenarioOutcome).GetCustomAttribute<AliasAttribute>()?.Alias);
        AssertShape<FeatureScenarioEvidence>("digitalbrain.feature.scenario-evidence.v1",
            (0, "ScenarioId", typeof(string)),
            (1, "Name", typeof(string)),
            (2, "Outcome", typeof(FeatureScenarioOutcome)),
            (3, "SafeFailure", typeof(string)),
            (4, "DurationMilliseconds", typeof(long)));
        AssertShape<FeatureVerificationArtifact>("digitalbrain.feature.verification-artifact.v1",
            (0, "Name", typeof(string)),
            (1, "MediaType", typeof(string)),
            (2, "SizeBytes", typeof(long)),
            (3, "Digest", typeof(string)));
        AssertShape<FeatureVerificationEvidence>("digitalbrain.feature.verification-evidence.v1",
            (0, "SourceReference", typeof(string)),
            (1, "Total", typeof(int)),
            (2, "Passed", typeof(int)),
            (3, "Failed", typeof(int)),
            (4, "Skipped", typeof(int)),
            (5, "Scenarios", typeof(FeatureScenarioEvidence[])),
            (6, "Artifacts", typeof(FeatureVerificationArtifact[])));
        AssertShape<VerifiedFeatureCandidate>("digitalbrain.feature.verified-candidate.v1",
            (0, "Draft", typeof(FeatureDraft)),
            (1, "Release", typeof(FeatureReleaseMetadata)),
            (2, "Evidence", typeof(FeatureVerificationEvidence)));
        AssertShape<PrepareFeatureAccessReview>("digitalbrain.feature.prepare-access-review.v1",
            (0, "DraftId", typeof(FeatureDraftId)),
            (1, "ExpectedRevision", typeof(long)),
            (2, "InstallationId", typeof(FeatureInstallationId)),
            (3, "Release", typeof(ReleaseDigest)),
            (4, "Grants", typeof(FeatureGrantSpec[])),
            (5, "Subscriptions", typeof(string[])));
        AssertShape<FeatureAccessReview>("digitalbrain.feature.access-review.v1",
            (0, "Candidate", typeof(VerifiedFeatureCandidate)),
            (1, "InstallationId", typeof(FeatureInstallationId)),
            (2, "Grants", typeof(FeatureGrantSpec[])),
            (3, "Subscriptions", typeof(string[])),
            (4, "PreviousRelease", typeof(FeatureReleaseMetadata)));
        AssertShape<InstallFeatureVersion>("digitalbrain.feature.install-version.v1",
            (0, "DraftId", typeof(FeatureDraftId)),
            (1, "ExpectedRevision", typeof(long)),
            (2, "InstallationId", typeof(FeatureInstallationId)),
            (3, "Release", typeof(ReleaseDigest)),
            (4, "Grants", typeof(FeatureGrantSpec[])),
            (5, "Subscriptions", typeof(string[])),
            (6, "DecisionId", typeof(string)),
            (7, "IdempotencyId", typeof(string)),
            (8, "RuntimeRevision", typeof(long?)),
            (9, "RuntimeActiveRelease", typeof(ReleaseDigest?)),
            (10, "RuntimePreviousRelease", typeof(ReleaseDigest?)));
        AssertShape<FeatureDraftInstallationReservation>("digitalbrain.feature.draft-installation-reservation.v1",
            (0, "DraftId", typeof(FeatureDraftId)),
            (1, "DraftRevision", typeof(long)),
            (2, "InstallationId", typeof(FeatureInstallationId)),
            (3, "Release", typeof(ReleaseDigest)),
            (4, "IdempotencyId", typeof(string)),
            (5, "CommandDigest", typeof(string)),
            (6, "AccessDigest", typeof(string)),
            (7, "DecisionId", typeof(string)),
            (8, "ActorId", typeof(ActorId)),
            (9, "Grants", typeof(FeatureGrantSpec[])),
            (10, "Subscriptions", typeof(string[])),
            (11, "RuntimeRevision", typeof(long?)),
            (12, "RuntimeActiveRelease", typeof(ReleaseDigest?)),
            (13, "RuntimePreviousRelease", typeof(ReleaseDigest?)),
            (14, "AuthorityBaseline", typeof(FeatureInstallationAuthorityBaseline)));
        AssertShape<FeatureInstallationAuthorityBaseline>("digitalbrain.feature.installation-authority-baseline.v1",
            (0, "InstallationId", typeof(FeatureInstallationId)),
            (1, "ActorId", typeof(ActorId)),
            (2, "ActiveRelease", typeof(ReleaseDigest)),
            (3, "PreviousRelease", typeof(ReleaseDigest?)),
            (4, "ActiveGrantRevision", typeof(GrantRevision)),
            (5, "ActiveGrants", typeof(FeatureGrantSpec[])),
            (6, "PreviousGrantRevision", typeof(GrantRevision?)),
            (7, "PreviousGrants", typeof(FeatureGrantSpec[])),
            (8, "Paused", typeof(bool)),
            (9, "PauseReason", typeof(string)),
            (10, "PublicationFence", typeof(long)),
            (11, "PublicationReceipt", typeof(FeaturePublicationReceipt)),
            (12, "PreviousSubscriptions", typeof(string[])),
            (13, "RollbackReplay", typeof(FeatureInstallationRollbackReplayBaseline)),
            (14, "Registration", typeof(FeatureInstallationRegistration)));
        AssertShape<FeatureInstallationRollbackReplayBaseline>("digitalbrain.feature.installation-rollback-replay-baseline.v1",
            (0, "InstallationId", typeof(FeatureInstallationId)),
            (1, "ExpectedActiveRelease", typeof(ReleaseDigest)),
            (2, "TargetRelease", typeof(ReleaseDigest)),
            (3, "ExpectedRevision", typeof(long)),
            (4, "IdempotencyId", typeof(string)),
            (5, "ResultAccessDigest", typeof(string)));
        AssertShape<FeaturePublicationTicket>("digitalbrain.feature.publication-ticket.v1",
            (0, "InstallationId", typeof(FeatureInstallationId)),
            (1, "ActorId", typeof(ActorId)),
            (2, "Release", typeof(ReleaseDigest)),
            (3, "GrantRevision", typeof(GrantRevision)),
            (4, "ActiveGrants", typeof(FeatureGrantSpec[])),
            (5, "Subscriptions", typeof(string[])),
            (6, "PublicationFence", typeof(long)),
            (7, "AuthorityDigest", typeof(string)),
            (8, "AccessDigest", typeof(string)));
        AssertShape<FeaturePublicationReceipt>("digitalbrain.feature.publication-receipt.v1",
            (0, "InstallationId", typeof(FeatureInstallationId)),
            (1, "PublicationFence", typeof(long)),
            (2, "AuthorityDigest", typeof(string)),
            (3, "AccessDigest", typeof(string)),
            (4, "ManifestDigest", typeof(string)));
        AssertShape<InstalledFeatureVersion>("digitalbrain.feature.installed-version.v1",
            (0, "Draft", typeof(FeatureDraft)),
            (1, "Release", typeof(FeatureReleaseMetadata)),
            (2, "Authority", typeof(FeatureAuthoritySnapshot)),
            (3, "Registration", typeof(FeatureInstallationRegistration)));
        AssertShape<RollbackFeatureVersion>("digitalbrain.feature.rollback-version.v1",
            (0, "DraftId", typeof(FeatureDraftId)),
            (1, "ExpectedActiveRelease", typeof(ReleaseDigest)),
            (2, "TargetRelease", typeof(ReleaseDigest)),
            (3, "IdempotencyId", typeof(string)),
            (4, "ExpectedRevision", typeof(long)));
        AssertShape<InstalledFeatureDetail>("digitalbrain.feature.installed-detail.v1",
            (0, "Draft", typeof(FeatureDraft)),
            (1, "ActiveRelease", typeof(FeatureReleaseMetadata)),
            (2, "PreviousRelease", typeof(FeatureReleaseMetadata)),
            (3, "Authority", typeof(FeatureAuthoritySnapshot)),
            (4, "Registration", typeof(FeatureInstallationRegistration)),
            (5, "Revision", typeof(long)));
    }

    [Fact]
    public void Feature_authority_snapshot_defaults_exact_rollback_to_false_for_legacy_construction()
    {
        var installationId = new FeatureInstallationId("installation-legacy-authority");
        var snapshot = new FeatureAuthoritySnapshot(
            installationId,
            new ActorId("actor-legacy-authority"),
            new ReleaseDigest(new string('a', 64)),
            new ReleaseDigest(new string('b', 64)),
            new GrantRevision(2),
            [],
            null,
            null,
            [],
            false,
            null);
        var property = typeof(FeatureAuthoritySnapshot).GetProperty("ExactRollbackAvailable");
        var publication = typeof(FeatureAuthoritySnapshot).GetProperty("PublicationConfirmed");

        Assert.NotNull(property);
        Assert.Equal(12u, Assert.IsType<IdAttribute>(property.GetCustomAttribute<IdAttribute>()).Id);
        Assert.False(Assert.IsType<bool>(property.GetValue(snapshot)));
        Assert.NotNull(publication);
        Assert.Equal(13u, Assert.IsType<IdAttribute>(publication.GetCustomAttribute<IdAttribute>()).Id);
        Assert.False(Assert.IsType<bool>(publication.GetValue(snapshot)));
    }

    [Fact]
    public void Authoring_contracts_round_trip_through_the_Orleans_serializer()
    {
        var services = new ServiceCollection();
        services.AddSerializer(builder => builder.AddAssembly(typeof(FeatureDraftPatch).Assembly));
        using var provider = services.BuildServiceProvider();
        var values = Values();

        RoundTrip(provider, values.Patch);
        RoundTrip(provider, values.Suggest);
        RoundTrip(provider, values.Accept);
        RoundTrip(provider, values.Reject);
        RoundTrip(provider, values.Verify);
        RoundTrip(provider, values.Evidence);
        RoundTrip(provider, values.Candidate);
        RoundTrip(provider, values.Prepare);
        RoundTrip(provider, values.Review);
        RoundTrip(provider, values.Install);
        RoundTrip(provider, values.Reservation);
        RoundTrip(provider, values.PublicationTicket);
        RoundTrip(provider, values.PublicationReceipt);
        RoundTrip(provider, values.Installed);
        RoundTrip(provider, values.Rollback);
        RoundTrip(provider, values.Detail);
    }

    [Fact]
    public void Authoring_grain_method_aliases_are_exact()
    {
        Assert.Equal("digitalbrain.feature.suggestion-model-grain.v1", typeof(IFeatureSuggestionModelGrain).GetCustomAttribute<AliasAttribute>()?.Alias);
        AssertMethodAlias(typeof(IFeatureSuggestionModelGrain), nameof(IFeatureSuggestionModelGrain.SuggestAsync), "suggest");
        AssertMethodAlias(typeof(IFeatureHubGrain), nameof(IFeatureHubGrain.AcceptSuggestedChangeAsync), "accept-suggested-change");
        AssertMethodAlias(typeof(IFeatureHubGrain), nameof(IFeatureHubGrain.RejectSuggestedChangeAsync), "reject-suggested-change");
        AssertMethodAlias(typeof(IFeatureHubGrain), nameof(IFeatureHubGrain.ReadInstalledDraftAsync), "read-installed-draft");
        AssertMethodAlias(typeof(IFeatureHubGrain), nameof(IFeatureHubGrain.AcquireDraftInstallationReservationAsync), "acquire-draft-installation-reservation");
        AssertMethodAlias(typeof(IFeatureHubGrain), nameof(IFeatureHubGrain.ReadDraftInstallationReservationAsync), "read-draft-installation-reservation");
        AssertMethodAlias(typeof(IFeatureHubGrain), nameof(IFeatureHubGrain.PrepareActivePublicationAsync), "prepare-active-publication");
        AssertMethodAlias(typeof(IFeatureHubGrain), nameof(IFeatureHubGrain.ConfirmActivePublicationAsync), "confirm-active-publication");
        Assert.Equal(
            "digitalbrain.capability-catalog-projection-grain.v1",
            typeof(ICapabilityCatalogProjectionGrain).GetCustomAttribute<AliasAttribute>()?.Alias);
        AssertMethodAlias(
            typeof(ICapabilityCatalogProjectionGrain),
            nameof(ICapabilityCatalogProjectionGrain.ReadAsync),
            "read");
        Assert.Equal(
            typeof(Task<CapabilityDescriptor[]>),
            typeof(ICapabilityCatalogProjectionGrain)
                .GetMethod(nameof(ICapabilityCatalogProjectionGrain.ReadAsync))!
                .ReturnType);

        var suggest = typeof(IFeatureSuggestionModelGrain).GetMethod(nameof(IFeatureSuggestionModelGrain.SuggestAsync))!;
        Assert.Equal(typeof(Task<FeatureDraftPatch>), suggest.ReturnType);
        Assert.Equal([typeof(SuggestFeatureChange), typeof(CancellationToken)], suggest.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.True(suggest.GetParameters()[1].IsOptional);
        var acquire = typeof(IFeatureHubGrain).GetMethod(nameof(IFeatureHubGrain.AcquireDraftInstallationReservationAsync))!;
        Assert.Equal(typeof(Task<FeatureDraftInstallationReservation>), acquire.ReturnType);
        Assert.Equal([typeof(InstallFeatureVersion), typeof(ActorId)], acquire.GetParameters().Select(parameter => parameter.ParameterType));
        var read = typeof(IFeatureHubGrain).GetMethod(nameof(IFeatureHubGrain.ReadDraftInstallationReservationAsync))!;
        Assert.Equal(typeof(Task<FeatureDraftInstallationReservation>), read.ReturnType);
        Assert.Equal([typeof(FeatureDraftId)], read.GetParameters().Select(parameter => parameter.ParameterType));
        var readInstalled = typeof(IFeatureHubGrain).GetMethod(nameof(IFeatureHubGrain.ReadInstalledDraftAsync))!;
        Assert.Equal(typeof(Task<FeatureDraft>), readInstalled.ReturnType);
        Assert.Equal(
            [typeof(FeatureInstallationId), typeof(ReleaseDigest)],
            readInstalled.GetParameters().Select(parameter => parameter.ParameterType));
        var preparePublication = typeof(IFeatureHubGrain).GetMethod(nameof(IFeatureHubGrain.PrepareActivePublicationAsync))!;
        Assert.Equal(typeof(Task<FeaturePublicationTicket>), preparePublication.ReturnType);
        Assert.Equal([typeof(FeatureInstallationId)], preparePublication.GetParameters().Select(parameter => parameter.ParameterType));
        var confirmPublication = typeof(IFeatureHubGrain).GetMethod(nameof(IFeatureHubGrain.ConfirmActivePublicationAsync))!;
        Assert.Equal(typeof(Task<FeaturePublicationReceipt>), confirmPublication.ReturnType);
        Assert.Equal([typeof(FeaturePublicationReceipt)], confirmPublication.GetParameters().Select(parameter => parameter.ParameterType));
    }

    private static void AssertShape<T>(string alias, params (uint Id, string Name, Type Type)[] expected)
    {
        var type = typeof(T);
        Assert.True(type.IsPublic && type.IsClass && type.IsSealed);
        Assert.NotNull(type.GetCustomAttribute<GenerateSerializerAttribute>());
        Assert.Equal(alias, type.GetCustomAttribute<AliasAttribute>()?.Alias);
        var actual = type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(property => (Id: Assert.IsType<IdAttribute>(property.GetCustomAttribute<IdAttribute>()).Id, property.Name, Type: property.PropertyType))
            .OrderBy(field => field.Id)
            .ToArray();
        Assert.Equal(expected, actual);
        var constructor = Assert.Single(type.GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.Equal(expected.Select(field => field.Type), constructor.GetParameters().Select(parameter => parameter.ParameterType));
    }

    private static void AssertMethodAlias(Type type, string methodName, string alias) =>
        Assert.Equal(alias, type.GetMethod(methodName)!.GetCustomAttribute<AliasAttribute>()?.Alias);

    private static void RoundTrip<T>(IServiceProvider provider, T value)
    {
        var serializer = provider.GetRequiredService<Serializer<T>>();
        var roundTrip = serializer.Deserialize(serializer.SerializeToArray(value));
        Assert.Equal(JsonSerializer.Serialize(value), JsonSerializer.Serialize(roundTrip));
    }

    private static ContractValues Values()
    {
        var now = new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);
        var draftId = new FeatureDraftId("draft-contract");
        var digest = new ReleaseDigest(new string('a', 64));
        var previousDigest = new ReleaseDigest(new string('9', 64));
        var installationId = new FeatureInstallationId("installation-contract");
        var behavior = new FeatureBehavior([new FeatureScenario("scenario-contract", "Contract", "a Draft exists", "it is serialized", "its shape is retained")]);
        var source = new FeatureSourceSnapshot(
            "src/Feature/Feature.csproj",
            "tests/Feature.Scenarios/Feature.Scenarios.csproj",
            [
                new FeatureSourceFile("src/Feature/Feature.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>"),
                new FeatureSourceFile("tests/Feature.Scenarios/Feature.Scenarios.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>")
            ]);
        var release = new FeatureReleaseMetadata(digest, $"sha256:{digest.Value}", FeatureSourceKind.RuntimeAuthored, ["capability.read"], []);
        var previousRelease = new FeatureReleaseMetadata(previousDigest, $"sha256:{previousDigest.Value}", FeatureSourceKind.RuntimeAuthored, ["capability.read"], []);
        var verification = new FeatureVerification(digest, 1, 1, 0, 0, now);
        var draft = new FeatureDraft(draftId, new OriginatingRequest("operation-contract", "conversation-contract", "Create a Feature"), "Create a Feature", "draft", behavior, source, verification, null, 1, now, now);
        var evidence = new FeatureVerificationEvidence(
            release.SourceReference,
            1,
            1,
            0,
            0,
            [new FeatureScenarioEvidence("scenario-contract", "Contract", FeatureScenarioOutcome.Passed, null, 12)],
            [new FeatureVerificationArtifact("scenarios.json", "application/json", 128, "sha256:" + new string('f', 64))]);
        var grants = new[] { new FeatureGrantSpec("capability.read", 1, null, "{\"allowedToolIds\":[\"capability.read\"]}") };
        var subscriptions = new[] { "conversation.completed" };
        var patch = new FeatureDraftPatch("patch-contract", draftId, 1, "Replace the Draft", behavior, source);
        var candidate = new VerifiedFeatureCandidate(draft, release, evidence);
        var registration = new FeatureInstallationRegistration(installationId, digest, subscriptions);
        var authority = new FeatureAuthoritySnapshot(installationId, new ActorId("actor-contract"), digest, null, new GrantRevision(1), grants, null, null, [], false, null);
        var activeGrantRevision = authority.ActiveGrantRevision ?? throw new InvalidOperationException();
        var install = new InstallFeatureVersion(draftId, 1, installationId, digest, grants, subscriptions, "decision-contract", "install-contract");
        var publicationTicket = new FeaturePublicationTicket(
            installationId,
            authority.ActorId,
            digest,
            activeGrantRevision,
            grants,
            subscriptions,
            1,
            new string('c', 64),
            new string('d', 64));
        var publicationReceipt = new FeaturePublicationReceipt(
            installationId,
            publicationTicket.PublicationFence,
            publicationTicket.AuthorityDigest,
            publicationTicket.AccessDigest,
            new string('e', 64));
        return new ContractValues(
            patch,
            new SuggestFeatureChange(draftId, 1, "Improve the Feature", "suggest-contract"),
            new AcceptSuggestedChange(patch, 1, "accept-contract", now),
            new RejectSuggestedChange(draftId, patch.PatchId, 1, 1),
            new VerifyFeatureDraft(draftId, 1, "verify-contract"),
            evidence,
            candidate,
            new PrepareFeatureAccessReview(draftId, 1, installationId, digest, grants, subscriptions),
            new FeatureAccessReview(candidate, installationId, grants, subscriptions, previousRelease),
            install,
            new FeatureDraftInstallationReservation(draftId, 1, installationId, digest, install.IdempotencyId, new string('b', 64), publicationTicket.AccessDigest, install.DecisionId, authority.ActorId),
            publicationTicket,
            publicationReceipt,
            new InstalledFeatureVersion(draft, release, authority, registration),
            new RollbackFeatureVersion(draftId, digest, previousDigest, "rollback-contract", 7),
            new InstalledFeatureDetail(draft, release, previousRelease, authority, registration, 7));
    }

    private sealed record ContractValues(
        FeatureDraftPatch Patch,
        SuggestFeatureChange Suggest,
        AcceptSuggestedChange Accept,
        RejectSuggestedChange Reject,
        VerifyFeatureDraft Verify,
        FeatureVerificationEvidence Evidence,
        VerifiedFeatureCandidate Candidate,
        PrepareFeatureAccessReview Prepare,
        FeatureAccessReview Review,
        InstallFeatureVersion Install,
        FeatureDraftInstallationReservation Reservation,
        FeaturePublicationTicket PublicationTicket,
        FeaturePublicationReceipt PublicationReceipt,
        InstalledFeatureVersion Installed,
        RollbackFeatureVersion Rollback,
        InstalledFeatureDetail Detail);
}
