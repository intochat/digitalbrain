using System.Reflection;
using System.Text.Json;
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
        AssertShape<VerifiedFeatureCandidate>("digitalbrain.feature.verified-candidate.v1",
            (0, "Draft", typeof(FeatureDraft)),
            (1, "Release", typeof(FeatureReleaseMetadata)));
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
            (3, "Subscriptions", typeof(string[])));
        AssertShape<InstallFeatureVersion>("digitalbrain.feature.install-version.v1",
            (0, "DraftId", typeof(FeatureDraftId)),
            (1, "ExpectedRevision", typeof(long)),
            (2, "InstallationId", typeof(FeatureInstallationId)),
            (3, "Release", typeof(ReleaseDigest)),
            (4, "Grants", typeof(FeatureGrantSpec[])),
            (5, "Subscriptions", typeof(string[])),
            (6, "DecisionId", typeof(string)),
            (7, "IdempotencyId", typeof(string)));
        AssertShape<FeatureDraftInstallationReservation>("digitalbrain.feature.draft-installation-reservation.v1",
            (0, "DraftId", typeof(FeatureDraftId)),
            (1, "DraftRevision", typeof(long)),
            (2, "InstallationId", typeof(FeatureInstallationId)),
            (3, "Release", typeof(ReleaseDigest)),
            (4, "IdempotencyId", typeof(string)),
            (5, "CommandDigest", typeof(string)),
            (6, "AccessDigest", typeof(string)),
            (7, "DecisionId", typeof(string)),
            (8, "ActorId", typeof(ActorId)));
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
        RoundTrip(provider, values.Candidate);
        RoundTrip(provider, values.Prepare);
        RoundTrip(provider, values.Review);
        RoundTrip(provider, values.Install);
        RoundTrip(provider, values.Reservation);
        RoundTrip(provider, values.PublicationTicket);
        RoundTrip(provider, values.PublicationReceipt);
        RoundTrip(provider, values.Installed);
    }

    [Fact]
    public void Authoring_grain_method_aliases_are_exact()
    {
        Assert.Equal("digitalbrain.feature.suggestion-model-grain.v1", typeof(IFeatureSuggestionModelGrain).GetCustomAttribute<AliasAttribute>()?.Alias);
        AssertMethodAlias(typeof(IFeatureSuggestionModelGrain), nameof(IFeatureSuggestionModelGrain.SuggestAsync), "suggest");
        AssertMethodAlias(typeof(IFeatureHubGrain), nameof(IFeatureHubGrain.AcceptSuggestedChangeAsync), "accept-suggested-change");
        AssertMethodAlias(typeof(IFeatureHubGrain), nameof(IFeatureHubGrain.RejectSuggestedChangeAsync), "reject-suggested-change");
        AssertMethodAlias(typeof(IFeatureHubGrain), nameof(IFeatureHubGrain.AcquireDraftInstallationReservationAsync), "acquire-draft-installation-reservation");
        AssertMethodAlias(typeof(IFeatureHubGrain), nameof(IFeatureHubGrain.ReadDraftInstallationReservationAsync), "read-draft-installation-reservation");
        AssertMethodAlias(typeof(IFeatureHubGrain), nameof(IFeatureHubGrain.PrepareActivePublicationAsync), "prepare-active-publication");
        AssertMethodAlias(typeof(IFeatureHubGrain), nameof(IFeatureHubGrain.ConfirmActivePublicationAsync), "confirm-active-publication");

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
        var verification = new FeatureVerification(digest, 1, 1, 0, 0, now);
        var draft = new FeatureDraft(draftId, new OriginatingRequest("operation-contract", "conversation-contract", "Create a Feature"), "Create a Feature", "draft", behavior, source, verification, null, 1, now, now);
        var grants = new[] { new FeatureGrantSpec("capability.read", 1, null, "{\"allowedToolIds\":[\"capability.read\"]}") };
        var subscriptions = new[] { "conversation.completed" };
        var patch = new FeatureDraftPatch("patch-contract", draftId, 1, "Replace the Draft", behavior, source);
        var candidate = new VerifiedFeatureCandidate(draft, release);
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
            candidate,
            new PrepareFeatureAccessReview(draftId, 1, installationId, digest, grants, subscriptions),
            new FeatureAccessReview(candidate, installationId, grants, subscriptions),
            install,
            new FeatureDraftInstallationReservation(draftId, 1, installationId, digest, install.IdempotencyId, new string('b', 64), publicationTicket.AccessDigest, install.DecisionId, authority.ActorId),
            publicationTicket,
            publicationReceipt,
            new InstalledFeatureVersion(draft, release, authority, registration));
    }

    private sealed record ContractValues(
        FeatureDraftPatch Patch,
        SuggestFeatureChange Suggest,
        AcceptSuggestedChange Accept,
        RejectSuggestedChange Reject,
        VerifyFeatureDraft Verify,
        VerifiedFeatureCandidate Candidate,
        PrepareFeatureAccessReview Prepare,
        FeatureAccessReview Review,
        InstallFeatureVersion Install,
        FeatureDraftInstallationReservation Reservation,
        FeaturePublicationTicket PublicationTicket,
        FeaturePublicationReceipt PublicationReceipt,
        InstalledFeatureVersion Installed);
}
