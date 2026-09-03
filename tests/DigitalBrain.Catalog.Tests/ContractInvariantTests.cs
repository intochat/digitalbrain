using System.Text.Json;
using DigitalBrain.Abstractions.Identity;
using Xunit;

namespace DigitalBrain.Catalog.Tests;

public sealed class ContractInvariantTests
{
    private static readonly OwnerId Owner = new("owner-a");
    private static readonly CatalogFingerprint Fingerprint = new(new string('a', 64));

    [Fact]
    public void TargetScopeSeparatesPlatformDefinitionsFromOwnerResources()
    {
        Assert.Throws<ArgumentException>(() => Descriptor(
            CatalogEntryKind.Module,
            CatalogScope.ForOwner(Owner),
            CatalogTypedReference.ForStable(CatalogTargetKind.Module, "module.fixture")));

        Assert.Throws<ArgumentException>(() => Descriptor(
            CatalogEntryKind.Script,
            CatalogScope.Platform,
            CatalogTypedReference.ForDurable(
                CatalogTargetKind.Script,
                new DurableInspectionReference("script", "script-a", "1"))));

        Assert.Throws<ArgumentException>(() => Descriptor(
            CatalogEntryKind.Script,
            CatalogScope.ForOwner(Owner),
            CatalogTypedReference.ForDurable(
                CatalogTargetKind.Script,
                new DurableInspectionReference("automation", "script-a", "1"))));

        var descriptor = Descriptor(
            CatalogEntryKind.Script,
            CatalogScope.ForOwner(Owner),
            CatalogTypedReference.ForDurable(
                CatalogTargetKind.Script,
                new DurableInspectionReference("script", "script-a", "1")));

        Assert.Equal(CatalogScopeKind.Owner, descriptor.Reference.Scope.Kind);
    }

    [Fact]
    public void DescriptorMetadataIsClosedOverItsKind()
    {
        var module = CatalogFixtures.ModuleDescriptor();
        var invalid = module with
        {
            Signal = new CatalogSignalDescriptor("db.signal.fixture", Schema()),
        };

        Assert.Throws<ArgumentException>(invalid.Validate);
    }

    [Fact]
    public void ProjectionAndContributionBoundariesRevalidateDescriptors()
    {
        var descriptor = CatalogFixtures.ModuleDescriptor();
        var invalid = descriptor with
        {
            Target = CatalogTypedReference.ForStable(CatalogTargetKind.Capability, "capability.fixture"),
        };
        var partition = new CatalogSourcePartition(
            descriptor.Reference.Source.Kind,
            "configured-modules",
            descriptor.Reference.Scope);

        Assert.Throws<ArgumentException>(() => new CatalogContribution("Fixture.Module", [invalid]));
        Assert.Throws<ArgumentException>(() => new CatalogSourceSnapshotItem(CatalogSourcePosition.First, invalid));
        Assert.Throws<ArgumentException>(() => CatalogMutation.Upsert(
            Guid.NewGuid(), partition, invalid, CatalogSourcePosition.First));
    }

    [Fact]
    public void CanonicalCollectionsCannotBeMutatedThroughTheirPublicInterfaces()
    {
        var discovery = new CatalogDiscoveryText(
            ["fixture"], null, null, null, null, null, null);
        var descriptor = CatalogFixtures.ModuleDescriptor();
        var contribution = new CatalogContribution("Fixture.Module", [descriptor]);
        var page = new CatalogSourceSnapshotPage(
            "snapshot-a",
            CatalogSourcePosition.First,
            [new CatalogSourceSnapshotItem(CatalogSourcePosition.First, descriptor)],
            continuationToken: null);
        var query = new DiscoveryQuery(
            "fixture", [CatalogEntryKind.Module], null, null,
            CatalogAvailabilityRequirement.Any, 1, null);

        AssertReadOnly(discovery.Aliases, "changed");
        AssertReadOnly(contribution.Descriptors, CatalogFixtures.ModuleDescriptor("module.changed"));
        AssertReadOnly(page.Items, new CatalogSourceSnapshotItem(
            CatalogSourcePosition.First, CatalogFixtures.ModuleDescriptor("module.changed")));
        AssertReadOnly(query.Kinds!, CatalogEntryKind.Operation);
    }

    [Fact]
    public void OriginHasExactlyOneValidFirstSuccessor()
    {
        Assert.True(CatalogSourcePosition.First.IsImmediateSuccessorOf(CatalogSourcePosition.Origin));
        Assert.False(new CatalogSourcePosition(1, 1).IsImmediateSuccessorOf(CatalogSourcePosition.Origin));
        Assert.True(new CatalogSourcePosition(1, 1).IsImmediateSuccessorOf(new CatalogSourcePosition(0, 5)));
    }

    [Fact]
    public void SnapshotTokensAreOpaqueAndBlankContinuationCannotMeanTerminal()
    {
        Assert.Throws<ArgumentException>(() => new CatalogSourceSnapshotPage(
            "snapshot-a", CatalogSourcePosition.Origin, null, " "));
        Assert.Throws<ArgumentException>(() => new CatalogSourceSnapshotPage(
            "snapshot-a", CatalogSourcePosition.Origin, null, " next "));
        Assert.Throws<ArgumentException>(() => new CatalogSourceSnapshotPage(
            " snapshot-a ", CatalogSourcePosition.Origin, null, null));
    }

    [Theory]
    [InlineData(DiscoveryStatus.SemanticDegraded)]
    [InlineData(DiscoveryStatus.Initializing)]
    [InlineData(DiscoveryStatus.StaleCursor)]
    public void OnlyReadyDiscoveryMayCarryANextCursor(DiscoveryStatus status)
        => Assert.Throws<ArgumentException>(() => new DiscoveryResult(
            status,
            null,
            Diagnostics(semanticGenerationId: "generation-a", semanticSnapshotToken: "semantic-a"),
            "cursor-a"));

    [Fact]
    public void ReadyCursorRequiresACompleteSemanticSnapshotIdentity()
        => Assert.Throws<ArgumentException>(() => new DiscoveryResult(
            DiscoveryStatus.Ready,
            null,
            Diagnostics(semanticGenerationId: null, semanticSnapshotToken: null),
            "cursor-a"));

    [Theory]
    [InlineData(DiscoveryStatus.Initializing)]
    [InlineData(DiscoveryStatus.StaleCursor)]
    public void NonSnapshotDiscoveryStatusesCannotCarryCandidates(DiscoveryStatus status)
        => Assert.Throws<ArgumentException>(() => new DiscoveryResult(
            status,
            [Candidate()],
            Diagnostics(semanticGenerationId: null, semanticSnapshotToken: null),
            null));

    [Fact]
    public void DiscoveryInputsAndEvidenceRejectUndefinedEnumsAndUnboundedData()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DiscoveryQuery(
            "fixture", [(CatalogEntryKind)int.MaxValue], null, null,
            CatalogAvailabilityRequirement.Any, 1, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DiscoveryCompatibility(
            null, null, null, null, null, null, null, null, null, null,
            (CatalogLifecycle)int.MaxValue, null, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DiscoveryEvidence(
            (DiscoveryExactMatchKind)int.MaxValue,
            DiscoveryCompatibilityEvidence.None,
            null,
            null,
            null,
            null,
            null,
            null));
        Assert.Throws<ArgumentException>(() => new DiscoveryEvidence(
            DiscoveryExactMatchKind.None,
            DiscoveryCompatibilityEvidence.None,
            null,
            null,
            null,
            null,
            Enumerable.Range(0, CatalogContractLimits.DiscoveryEvidenceItems + 1)
                .Select(static value => $"field-{value}")
                .ToArray(),
            null));
        Assert.Throws<ArgumentException>(() => new CatalogAvailabilitySnapshot(
            CatalogAvailabilityStatus.Degraded,
            DateTimeOffset.UtcNow,
            new string('x', CatalogContractLimits.ReasonLength + 1)));
        Assert.Throws<ArgumentException>(() => Diagnostics(
            semanticGenerationId: null,
            semanticSnapshotToken: null,
            degradationReason: new string('x', CatalogContractLimits.ReasonLength + 1)));
    }

    [Fact]
    public void DerivedHelpersAreNotPartOfJsonWireShape()
    {
        Assert.DoesNotContain("SortKey", JsonSerializer.Serialize(CatalogScope.Platform), StringComparison.Ordinal);
        Assert.DoesNotContain(
            "IsOrigin",
            JsonSerializer.Serialize(CatalogSourcePosition.Origin),
            StringComparison.Ordinal);
    }

    [Fact]
    public void CatalogInspectionNeverLeaksAReplacementAndRequiresExactPayloads()
    {
        var descriptor = CatalogFixtures.ModuleDescriptor();
        var availability = CatalogAvailabilitySnapshot.Unknown(DateTimeOffset.UtcNow);
        var replacement = CatalogFixtures.ModuleDescriptor("module.replacement");
        var retired = descriptor with { Lifecycle = CatalogLifecycle.Retired };

        Assert.Throws<ArgumentNullException>(() => new CatalogInspection(
            descriptor.Reference, CatalogInspectionStatus.Found, null, availability, null));
        Assert.Throws<ArgumentException>(() => new CatalogInspection(
            descriptor.Reference, CatalogInspectionStatus.Found, replacement, availability, null));
        Assert.Throws<ArgumentException>(() => new CatalogInspection(
            descriptor.Reference, CatalogInspectionStatus.StaleDescriptor, replacement, null, null));
        Assert.Throws<ArgumentException>(() => new CatalogInspection(
            descriptor.Reference, CatalogInspectionStatus.NotFound, null, availability, null));

        var inspection = new CatalogInspection(
            descriptor.Reference, CatalogInspectionStatus.Retired, retired, null, null);
        Assert.Same(retired, inspection.Descriptor);
    }

    [Fact]
    public void GeneralInspectionEnvelopeKeepsReferenceStatusAndPayloadCoherent()
    {
        var descriptor = CatalogFixtures.ModuleDescriptor();
        var reference = InspectionReference.ForCatalog(descriptor.Reference);
        var catalog = new CatalogInspection(
            descriptor.Reference,
            CatalogInspectionStatus.Found,
            descriptor,
            CatalogAvailabilitySnapshot.Unknown(DateTimeOffset.UtcNow),
            null);

        Assert.Throws<ArgumentNullException>(() => new InspectionResult(
            reference, InspectionStatus.Found, null, null));
        Assert.Throws<ArgumentException>(() => new InspectionResult(
            reference, InspectionStatus.StaleReference, catalog, null));
        Assert.Throws<ArgumentException>(() => new InspectionResult(
            InspectionReference.ForNeuron(CatalogFixtures.Neuron("worker")),
            InspectionStatus.Found,
            catalog,
            null));
        Assert.Throws<ArgumentException>(() => new InspectionResult(
            reference,
            InspectionStatus.Found,
            catalog,
            new string('x', CatalogContractLimits.ReasonLength + 1)));

        var result = new InspectionResult(reference, InspectionStatus.Found, catalog, null);
        Assert.Same(catalog, result.Catalog);
    }

    [Fact]
    public void HandledSignalsHaveCanonicalSetSemantics()
    {
        var signal = new CatalogSignalReference("db.signal.fixture", Fingerprint.Value);
        var descriptor = new CatalogNeuronDescriptor(
            "db.neuron.fixture",
            "fixture-neuron",
            [signal, signal]);

        Assert.Single(descriptor.HandledSignals);
        AssertReadOnly(descriptor.HandledSignals, new CatalogSignalReference(
            "db.signal.other", Fingerprint.Value));
    }

    [Fact]
    public void DiscoveryEvidenceReservesAnExactLaneRank()
        => Assert.NotNull(typeof(DiscoveryEvidence).GetProperty("ExactRank"));

    private static CatalogDescriptor Descriptor(
        CatalogEntryKind kind,
        CatalogScope scope,
        CatalogTypedReference target)
    {
        var id = $"{kind.ToString().ToLowerInvariant()}.fixture";
        return new CatalogDescriptor(
            new CatalogReference(
                scope,
                new CatalogSourceReference("fixture", id),
                new CatalogEntryId(id),
                "revision-a",
                Fingerprint),
            kind,
            CatalogLifecycle.Active,
            CatalogVisibility.Discoverable,
            CatalogConfigurationState.Configured,
            "Fixture",
            "Fixture descriptor.",
            CatalogDiscoveryText.Empty,
            target,
            neuron: null,
            signal: null,
            capability: null,
            operation: null);
    }

    private static CatalogSchemaReference Schema()
        => new("db.schema.fixture", Fingerprint.Value, "{}", 1);

    private static DiscoveryCandidate Candidate()
    {
        var descriptor = CatalogFixtures.ModuleDescriptor();
        return new DiscoveryCandidate(
            descriptor.Reference,
            descriptor.Kind,
            descriptor.Name,
            descriptor.Summary,
            descriptor.Target,
            descriptor.Lifecycle,
            descriptor.ConfigurationState,
            CatalogAvailabilitySnapshot.Unknown(DateTimeOffset.UtcNow),
            1,
            0,
            new DiscoveryEvidence(
                DiscoveryExactMatchKind.None,
                DiscoveryCompatibilityEvidence.None,
                null,
                null,
                null,
                null,
                null,
                null));
    }

    private static DiscoveryDiagnostics Diagnostics(
        string? semanticGenerationId,
        string? semanticSnapshotToken,
        string? degradationReason = null)
        => new(
            1,
            Fingerprint.Value,
            1,
            "availability-a",
            semanticGenerationId,
            semanticSnapshotToken,
            false,
            degradationReason);

    private static void AssertReadOnly<T>(IReadOnlyList<T> values, T replacement)
    {
        Assert.False(values is T[]);
        var mutableView = Assert.IsAssignableFrom<IList<T>>(values);
        Assert.True(mutableView.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => mutableView[0] = replacement);
    }
}
