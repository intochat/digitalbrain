using System.Reflection;
using DigitalBrain.Abstractions.Identity;
using Orleans;
using Xunit;

namespace DigitalBrain.Catalog.Tests;

public sealed class ContractShapeTests
{
    [Fact]
    public void DiscoveryQueryCannotSupplyAnOwnerOrAnExecutableDelegate()
    {
        var properties = typeof(DiscoveryQuery).GetProperties();

        Assert.DoesNotContain(properties, static property =>
            property.PropertyType == typeof(OwnerId) ||
            Nullable.GetUnderlyingType(property.PropertyType) == typeof(OwnerId));
        Assert.DoesNotContain(properties, static property =>
            typeof(Delegate).IsAssignableFrom(property.PropertyType));
        Assert.DoesNotContain(properties, static property =>
            property.Name.Contains("Handler", StringComparison.Ordinal));
    }

    [Fact]
    public void CandidateCarriesAnExactRevisionAndFingerprint()
    {
        var reference = new CatalogReference(
            CatalogScope.Platform,
            new CatalogSourceReference("platform-module", "module.time"),
            new CatalogEntryId("operation.time.timer.start"),
            "time@0.1.0",
            new CatalogFingerprint(new string('a', 64)));

        Assert.Equal(CatalogScopeKind.Platform, reference.Scope.Kind);
        Assert.Equal("platform-module", reference.Source.Kind);
        Assert.Equal("time@0.1.0", reference.SourceRevision);
        Assert.Equal(64, reference.Fingerprint.Value.Length);
    }

    [Fact]
    public void DirectoryExposesOnlyDiscoveryAndInspection()
        => Assert.Equal(
            [nameof(ICatalogDirectory.Discover), nameof(ICatalogDirectory.Inspect)],
            typeof(ICatalogDirectory).GetMethods().Select(static method => method.Name).Order());

    [Fact]
    public void GeneralInspectionReferenceIsDiscriminatedAndOwnerIsNotAModelField()
    {
        var reference = InspectionReference.ForSynapse(
            CatalogFixtures.Neuron("source"), CatalogFixtures.Neuron("target"), "db.signal.note");

        Assert.Equal(InspectionReferenceKind.Synapse, reference.Kind);
        Assert.NotNull(reference.Synapse);
        Assert.Null(reference.Catalog);
        Assert.DoesNotContain(
            typeof(InspectionReference).GetProperties(),
            static property => property.PropertyType == typeof(OwnerId));
    }

    [Fact]
    public void DurableInspectionKindsAreNormalizedWithoutChangingResourceIdentity()
    {
        var durable = new DurableInspectionReference(" Automation ", " Job-A ", " revision-A ");
        var reference = InspectionReference.ForDurableResource(durable);

        Assert.Equal("automation", durable.ResourceKind);
        Assert.Equal("Job-A", durable.ResourceId);
        Assert.Equal("revision-A", durable.Revision);
        Assert.Equal(InspectionReferenceKind.DurableResource, reference.Kind);
        Assert.Same(durable, reference.Durable);
    }

    [Fact]
    public void SourcePositionReservesZeroZeroForOrigin()
    {
        Assert.Equal(new CatalogSourcePosition(0, 0), CatalogSourcePosition.Origin);
        Assert.Equal(new CatalogSourcePosition(0, 1), CatalogSourcePosition.First);
        Assert.Throws<ArgumentOutOfRangeException>(() => new CatalogSourcePosition(1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CatalogSourcePosition(-1, 1));
    }

    [Fact]
    public void InvalidDefaultIdentityCannotBecomeAnOwnerScopeOrTypedTarget()
    {
        Assert.Throws<ArgumentException>(() => CatalogScope.ForOwner(default));
        Assert.Throws<ArgumentException>(() => CatalogTypedReference.ForNeuron(default));
        Assert.Throws<ArgumentException>(() => CatalogTypedReference.ForEntity(default));
    }

    [Fact]
    public void FingerprintsAreExactLowercaseSha256Values()
    {
        var value = new string('a', 64);

        Assert.Equal(value, new CatalogFingerprint(value).Value);
        Assert.Throws<ArgumentException>(() => new CatalogFingerprint(value.ToUpperInvariant()));
        Assert.Throws<ArgumentException>(() => new CatalogFingerprint("abc"));
    }

    [Fact]
    public void DefaultInspectionProviderKeyCannotRegisterAsCatalogProvider()
        => Assert.Throws<ArgumentException>(() => default(InspectionProviderKey).Validate());

    [Fact]
    public void MutationFactoriesEnforceSourceScopeAndNonOriginPosition()
    {
        var descriptor = CatalogFixtures.ModuleDescriptor();
        var partition = new CatalogSourcePartition(
            descriptor.Reference.Source.Kind,
            "configured-modules",
            descriptor.Reference.Scope);

        var mutation = CatalogMutation.Upsert(
            Guid.NewGuid(), partition, descriptor, CatalogSourcePosition.First);
        var tombstone = CatalogMutation.Tombstone(
            Guid.NewGuid(), partition, descriptor.Reference, new CatalogSourcePosition(0, 2));

        Assert.Same(descriptor, mutation.Descriptor);
        Assert.Equal(CatalogMutationKind.Upsert, mutation.Kind);
        Assert.Null(tombstone.Descriptor);
        Assert.Equal(CatalogMutationKind.Tombstone, tombstone.Kind);
        Assert.Throws<ArgumentException>(() => CatalogMutation.Upsert(
            Guid.NewGuid(), partition, descriptor, CatalogSourcePosition.Origin));
        Assert.Throws<ArgumentException>(() => CatalogMutation.Upsert(
            Guid.NewGuid(),
            new CatalogSourcePartition("another-source", "configured-modules", CatalogScope.Platform),
            descriptor,
            CatalogSourcePosition.First));
    }

    [Fact]
    public void DescriptorValidationRejectsMismatchedTargetKindAndOwner()
    {
        var descriptor = CatalogFixtures.ModuleDescriptor();
        var mismatchedKind = descriptor with
        {
            Target = CatalogTypedReference.ForStable(CatalogTargetKind.Capability, "capability.fixture"),
        };
        var foreignNeuron = CatalogTypedReference.ForNeuron(
            new NeuronId("fixture-neuron", new OwnerId("owner-b"), "worker"));

        Assert.Throws<ArgumentException>(mismatchedKind.Validate);
        Assert.Throws<ArgumentException>(() => foreignNeuron.ValidateFor(
            CatalogEntryKind.NeuronInstance,
            CatalogScope.ForOwner(new OwnerId("owner-a"))));
    }

    [Fact]
    public void SnapshotPagesDefensivelyCopyMutableInputLists()
    {
        var items = new List<CatalogSourceSnapshotItem>
        {
            new(CatalogSourcePosition.First, CatalogFixtures.ModuleDescriptor()),
        };
        var page = new CatalogSourceSnapshotPage(
            "snapshot-a", CatalogSourcePosition.First, items, continuationToken: null);

        items.Clear();

        Assert.Single(page.Items);
    }

    [Fact]
    public void WireAliasesAreUniqueAndStable()
    {
        var expected = new Dictionary<Type, string>
        {
            [typeof(CatalogEntryId)] = "db.catalog.entry-id",
            [typeof(CatalogFingerprint)] = "db.catalog.fingerprint",
            [typeof(CatalogScopeKind)] = "db.catalog.scope-kind",
            [typeof(CatalogScope)] = "db.catalog.scope",
            [typeof(CatalogSourceReference)] = "db.catalog.source-reference",
            [typeof(CatalogReference)] = "db.catalog.reference",
            [typeof(CatalogEntryKind)] = "db.catalog.entry-kind",
            [typeof(CatalogLifecycle)] = "db.catalog.lifecycle",
            [typeof(CatalogVisibility)] = "db.catalog.visibility",
            [typeof(CatalogConfigurationState)] = "db.catalog.configuration-state",
            [typeof(CatalogDiscoveryText)] = "db.catalog.discovery-text",
            [typeof(CatalogRecoverySemantics)] = "db.catalog.recovery-semantics",
            [typeof(CatalogCapabilityDescriptor)] = "db.catalog.capability",
            [typeof(CatalogSchemaReference)] = "db.catalog.schema",
            [typeof(CatalogOperationDescriptor)] = "db.catalog.operation",
            [typeof(CatalogSignalDescriptor)] = "db.catalog.signal-contract",
            [typeof(CatalogSignalReference)] = "db.catalog.signal-reference",
            [typeof(CatalogNeuronDescriptor)] = "db.catalog.neuron",
            [typeof(CatalogTargetKind)] = "db.catalog.target-kind",
            [typeof(CatalogTypedReference)] = "db.catalog.typed-reference",
            [typeof(CatalogDescriptor)] = "db.catalog.descriptor",
            [typeof(CatalogAvailabilityStatus)] = "db.catalog.availability-status",
            [typeof(CatalogAvailabilityRequirement)] = "db.catalog.availability-requirement",
            [typeof(CatalogAvailabilitySnapshot)] = "db.catalog.availability-snapshot",
            [typeof(DiscoveryCompatibility)] = "db.catalog.discovery-compatibility",
            [typeof(DiscoveryQuery)] = "db.catalog.discovery-query",
            [typeof(DiscoveryExactMatchKind)] = "db.catalog.discovery-exact-match-kind",
            [typeof(DiscoveryCompatibilityEvidence)] = "db.catalog.compatibility-evidence",
            [typeof(DiscoveryEvidence)] = "db.catalog.discovery-evidence",
            [typeof(DiscoveryCandidate)] = "db.catalog.discovery-candidate",
            [typeof(DiscoveryStatus)] = "db.catalog.discovery-status",
            [typeof(DiscoveryDiagnostics)] = "db.catalog.discovery-diagnostics",
            [typeof(DiscoveryResult)] = "db.catalog.discovery-result",
            [typeof(CatalogInspectionStatus)] = "db.catalog.catalog-inspection-status",
            [typeof(CatalogInspection)] = "db.catalog.catalog-inspection",
            [typeof(InspectionReferenceKind)] = "db.catalog.inspection-reference-kind",
            [typeof(SynapseReference)] = "db.catalog.synapse-reference",
            [typeof(DurableInspectionReference)] = "db.catalog.durable-inspection-reference",
            [typeof(InspectionReference)] = "db.catalog.inspection-reference",
            [typeof(InspectionStatus)] = "db.catalog.inspection-status",
            [typeof(InspectionResult)] = "db.catalog.inspection-result",
            [typeof(CatalogSourcePosition)] = "db.catalog.source-position",
            [typeof(CatalogSourcePartition)] = "db.catalog.source-partition",
            [typeof(CatalogSourceSnapshot)] = "db.catalog.source-snapshot",
            [typeof(CatalogSourceSnapshotItem)] = "db.catalog.source-snapshot-item",
            [typeof(CatalogSourceSnapshotPage)] = "db.catalog.source-snapshot-page",
            [typeof(CatalogMutationKind)] = "db.catalog.mutation-kind",
            [typeof(CatalogMutation)] = "db.catalog.mutation",
            [typeof(CatalogContribution)] = "db.catalog.contribution",
        };

        var aliases = expected.Select(static item => AliasOf(item.Key)).ToArray();

        Assert.Equal(expected.Count, aliases.Distinct(StringComparer.Ordinal).Count());
        Assert.All(expected, static item => Assert.Equal(item.Value, AliasOf(item.Key)));
        Assert.All(expected.Keys, static type =>
            Assert.NotNull(type.GetCustomAttribute<GenerateSerializerAttribute>()));
    }

    [Fact]
    public void WireMembersKeepTheirAssignedFieldIds()
    {
        AssertIds<CatalogReference>(
            (nameof(CatalogReference.Scope), 0),
            (nameof(CatalogReference.Source), 1),
            (nameof(CatalogReference.Id), 2),
            (nameof(CatalogReference.SourceRevision), 3),
            (nameof(CatalogReference.Fingerprint), 4));
        AssertIds<CatalogEntryId>((nameof(CatalogEntryId.Value), 0));
        AssertIds<CatalogFingerprint>((nameof(CatalogFingerprint.Value), 0));
        AssertIds<CatalogScope>((nameof(CatalogScope.Kind), 0), (nameof(CatalogScope.Owner), 1));
        AssertIds<CatalogSourceReference>(
            (nameof(CatalogSourceReference.Kind), 0),
            (nameof(CatalogSourceReference.Id), 1));
        AssertIds<CatalogDiscoveryText>(
            (nameof(CatalogDiscoveryText.Aliases), 0),
            (nameof(CatalogDiscoveryText.Keywords), 1),
            (nameof(CatalogDiscoveryText.Tags), 2),
            (nameof(CatalogDiscoveryText.RoutingExamples), 3),
            (nameof(CatalogDiscoveryText.InputConcepts), 4),
            (nameof(CatalogDiscoveryText.OutputConcepts), 5),
            (nameof(CatalogDiscoveryText.WhenNotToUse), 6));
        AssertIds<CatalogCapabilityDescriptor>(
            (nameof(CatalogCapabilityDescriptor.CapabilityId), 0),
            (nameof(CatalogCapabilityDescriptor.Version), 1));
        AssertIds<CatalogSchemaReference>(
            (nameof(CatalogSchemaReference.SchemaId), 0),
            (nameof(CatalogSchemaReference.Sha256), 1),
            (nameof(CatalogSchemaReference.CanonicalJson), 2),
            (nameof(CatalogSchemaReference.FormatVersion), 3));
        AssertIds<CatalogOperationDescriptor>(
            (nameof(CatalogOperationDescriptor.OperationId), 0),
            (nameof(CatalogOperationDescriptor.Version), 1),
            (nameof(CatalogOperationDescriptor.CapabilityId), 2),
            (nameof(CatalogOperationDescriptor.CapabilityVersion), 3),
            (nameof(CatalogOperationDescriptor.Input), 4),
            (nameof(CatalogOperationDescriptor.Output), 5),
            (nameof(CatalogOperationDescriptor.Recovery), 6),
            (nameof(CatalogOperationDescriptor.BindingId), 7),
            (nameof(CatalogOperationDescriptor.BindingRevision), 8),
            (nameof(CatalogOperationDescriptor.RequiredScopes), 9));
        AssertIds<CatalogSignalDescriptor>(
            (nameof(CatalogSignalDescriptor.Alias), 0),
            (nameof(CatalogSignalDescriptor.Schema), 1));
        AssertIds<CatalogSignalReference>(
            (nameof(CatalogSignalReference.Alias), 0),
            (nameof(CatalogSignalReference.SchemaHash), 1));
        AssertIds<CatalogNeuronDescriptor>(
            (nameof(CatalogNeuronDescriptor.ContractAlias), 0),
            (nameof(CatalogNeuronDescriptor.GrainType), 1),
            (nameof(CatalogNeuronDescriptor.HandledSignals), 2));
        AssertIds<CatalogTypedReference>(
            (nameof(CatalogTypedReference.Kind), 0),
            (nameof(CatalogTypedReference.StableId), 1),
            (nameof(CatalogTypedReference.Neuron), 2),
            (nameof(CatalogTypedReference.Entity), 3),
            (nameof(CatalogTypedReference.Durable), 4));
        AssertIds<CatalogDescriptor>(
            (nameof(CatalogDescriptor.Reference), 0),
            (nameof(CatalogDescriptor.Kind), 1),
            (nameof(CatalogDescriptor.Lifecycle), 2),
            (nameof(CatalogDescriptor.Visibility), 3),
            (nameof(CatalogDescriptor.ConfigurationState), 4),
            (nameof(CatalogDescriptor.Name), 5),
            (nameof(CatalogDescriptor.Summary), 6),
            (nameof(CatalogDescriptor.Discovery), 7),
            (nameof(CatalogDescriptor.Target), 8),
            (nameof(CatalogDescriptor.Neuron), 9),
            (nameof(CatalogDescriptor.Signal), 10),
            (nameof(CatalogDescriptor.Capability), 11),
            (nameof(CatalogDescriptor.Operation), 12));
        AssertIds<DiscoveryQuery>(
            (nameof(DiscoveryQuery.Text), 0),
            (nameof(DiscoveryQuery.Kinds), 1),
            (nameof(DiscoveryQuery.RequiredTags), 2),
            (nameof(DiscoveryQuery.Compatibility), 3),
            (nameof(DiscoveryQuery.Availability), 4),
            (nameof(DiscoveryQuery.Limit), 5),
            (nameof(DiscoveryQuery.Cursor), 6));
        AssertIds<CatalogAvailabilitySnapshot>(
            (nameof(CatalogAvailabilitySnapshot.Status), 0),
            (nameof(CatalogAvailabilitySnapshot.ObservedAt), 1),
            (nameof(CatalogAvailabilitySnapshot.Reason), 2));
        AssertIds<DiscoveryCompatibility>(
            (nameof(DiscoveryCompatibility.OperationId), 0),
            (nameof(DiscoveryCompatibility.OperationVersion), 1),
            (nameof(DiscoveryCompatibility.CapabilityId), 2),
            (nameof(DiscoveryCompatibility.CapabilityVersion), 3),
            (nameof(DiscoveryCompatibility.SignalAlias), 4),
            (nameof(DiscoveryCompatibility.SignalSchemaHash), 5),
            (nameof(DiscoveryCompatibility.InputSchemaId), 6),
            (nameof(DiscoveryCompatibility.InputSchemaHash), 7),
            (nameof(DiscoveryCompatibility.OutputSchemaId), 8),
            (nameof(DiscoveryCompatibility.OutputSchemaHash), 9),
            (nameof(DiscoveryCompatibility.Lifecycle), 10),
            (nameof(DiscoveryCompatibility.ConfigurationState), 11),
            (nameof(DiscoveryCompatibility.RequireInvocable), 12));
        AssertIds<DiscoveryEvidence>(
            (nameof(DiscoveryEvidence.ExactMatch), 0),
            (nameof(DiscoveryEvidence.Compatibility), 1),
            (nameof(DiscoveryEvidence.LexicalRank), 2),
            (nameof(DiscoveryEvidence.SemanticRank), 3),
            (nameof(DiscoveryEvidence.SemanticSimilarity), 4),
            (nameof(DiscoveryEvidence.MatchedFields), 5),
            (nameof(DiscoveryEvidence.RankReasons), 6),
            (nameof(DiscoveryEvidence.ExactRank), 7));
        AssertIds<DiscoveryCandidate>(
            (nameof(DiscoveryCandidate.Reference), 0),
            (nameof(DiscoveryCandidate.Kind), 1),
            (nameof(DiscoveryCandidate.Name), 2),
            (nameof(DiscoveryCandidate.Summary), 3),
            (nameof(DiscoveryCandidate.Target), 4),
            (nameof(DiscoveryCandidate.Lifecycle), 5),
            (nameof(DiscoveryCandidate.ConfigurationState), 6),
            (nameof(DiscoveryCandidate.Availability), 7),
            (nameof(DiscoveryCandidate.FinalRank), 8),
            (nameof(DiscoveryCandidate.RrfScore), 9),
            (nameof(DiscoveryCandidate.Evidence), 10));
        AssertIds<DiscoveryDiagnostics>(
            (nameof(DiscoveryDiagnostics.MetadataWatermark), 0),
            (nameof(DiscoveryDiagnostics.MetadataSnapshotFingerprint), 1),
            (nameof(DiscoveryDiagnostics.AvailabilityWatermark), 2),
            (nameof(DiscoveryDiagnostics.AvailabilitySnapshotToken), 3),
            (nameof(DiscoveryDiagnostics.SemanticGenerationId), 4),
            (nameof(DiscoveryDiagnostics.SemanticSnapshotToken), 5),
            (nameof(DiscoveryDiagnostics.CandidatePoolTruncated), 6),
            (nameof(DiscoveryDiagnostics.DegradationReason), 7));
        AssertIds<DiscoveryResult>(
            (nameof(DiscoveryResult.Status), 0),
            (nameof(DiscoveryResult.Candidates), 1),
            (nameof(DiscoveryResult.Diagnostics), 2),
            (nameof(DiscoveryResult.NextCursor), 3));
        AssertIds<CatalogInspection>(
            (nameof(CatalogInspection.Reference), 0),
            (nameof(CatalogInspection.Status), 1),
            (nameof(CatalogInspection.Descriptor), 2),
            (nameof(CatalogInspection.Availability), 3),
            (nameof(CatalogInspection.Reason), 4));
        AssertIds<SynapseReference>(
            (nameof(SynapseReference.Source), 0),
            (nameof(SynapseReference.Target), 1),
            (nameof(SynapseReference.SignalType), 2));
        AssertIds<DurableInspectionReference>(
            (nameof(DurableInspectionReference.ResourceKind), 0),
            (nameof(DurableInspectionReference.ResourceId), 1),
            (nameof(DurableInspectionReference.Revision), 2));
        AssertIds<InspectionReference>(
            (nameof(InspectionReference.Kind), 0),
            (nameof(InspectionReference.Catalog), 1),
            (nameof(InspectionReference.Neuron), 2),
            (nameof(InspectionReference.Synapse), 3),
            (nameof(InspectionReference.Entity), 4),
            (nameof(InspectionReference.Durable), 5));
        AssertIds<InspectionResult>(
            (nameof(InspectionResult.Reference), 0),
            (nameof(InspectionResult.Status), 1),
            (nameof(InspectionResult.Catalog), 2),
            (nameof(InspectionResult.Reason), 3));
        AssertIds<CatalogSourcePosition>(
            (nameof(CatalogSourcePosition.Epoch), 0),
            (nameof(CatalogSourcePosition.Sequence), 1));
        AssertIds<CatalogSourcePartition>(
            (nameof(CatalogSourcePartition.SourceKind), 0),
            (nameof(CatalogSourcePartition.PartitionId), 1),
            (nameof(CatalogSourcePartition.Scope), 2));
        AssertIds<CatalogSourceSnapshot>(
            (nameof(CatalogSourceSnapshot.Partition), 0),
            (nameof(CatalogSourceSnapshot.SnapshotToken), 1),
            (nameof(CatalogSourceSnapshot.HighWatermark), 2));
        AssertIds<CatalogSourceSnapshotItem>(
            (nameof(CatalogSourceSnapshotItem.Position), 0),
            (nameof(CatalogSourceSnapshotItem.Descriptor), 1));
        AssertIds<CatalogSourceSnapshotPage>(
            (nameof(CatalogSourceSnapshotPage.SnapshotToken), 0),
            (nameof(CatalogSourceSnapshotPage.HighWatermark), 1),
            (nameof(CatalogSourceSnapshotPage.Items), 2),
            (nameof(CatalogSourceSnapshotPage.ContinuationToken), 3));
        AssertIds<CatalogMutation>(
            (nameof(CatalogMutation.MutationId), 0),
            (nameof(CatalogMutation.Partition), 1),
            (nameof(CatalogMutation.Position), 2),
            (nameof(CatalogMutation.Reference), 3),
            (nameof(CatalogMutation.Kind), 4),
            (nameof(CatalogMutation.Descriptor), 5));
        AssertIds<CatalogContribution>(
            (nameof(CatalogContribution.ModuleTypeName), 0),
            (nameof(CatalogContribution.Descriptors), 1));
    }

    [Fact]
    public void DirectoryAndMethodsKeepTheirStableAliases()
    {
        Assert.Equal("db.catalog.directory", AliasOf(typeof(ICatalogDirectory)));
        Assert.Equal("Discover", AliasOf(typeof(ICatalogDirectory).GetMethod(nameof(ICatalogDirectory.Discover))!));
        Assert.Equal("Inspect", AliasOf(typeof(ICatalogDirectory).GetMethod(nameof(ICatalogDirectory.Inspect))!));
    }

    [Fact]
    public void WireEnumsHaveExplicitStableValues()
    {
        AssertEnumValues(
            (CatalogEntryKind.Module, 0),
            (CatalogEntryKind.Capability, 1),
            (CatalogEntryKind.NeuronType, 2),
            (CatalogEntryKind.NeuronInstance, 3),
            (CatalogEntryKind.SignalContract, 4),
            (CatalogEntryKind.Operation, 5),
            (CatalogEntryKind.Script, 6),
            (CatalogEntryKind.Automation, 7),
            (CatalogEntryKind.AgentDefinition, 8),
            (CatalogEntryKind.Entity, 9),
            (CatalogEntryKind.Activity, 10));
        AssertEnumValues(
            (CatalogTargetKind.Module, 0),
            (CatalogTargetKind.Capability, 1),
            (CatalogTargetKind.NeuronType, 2),
            (CatalogTargetKind.NeuronInstance, 3),
            (CatalogTargetKind.SignalContract, 4),
            (CatalogTargetKind.Operation, 5),
            (CatalogTargetKind.Script, 6),
            (CatalogTargetKind.Automation, 7),
            (CatalogTargetKind.AgentDefinition, 8),
            (CatalogTargetKind.Entity, 9),
            (CatalogTargetKind.Activity, 10));
        AssertEnumValues(
            (CatalogLifecycle.Draft, 0),
            (CatalogLifecycle.Active, 1),
            (CatalogLifecycle.Suspended, 2),
            (CatalogLifecycle.Retired, 3));
        AssertEnumValues(
            (CatalogVisibility.Discoverable, 0),
            (CatalogVisibility.InspectOnly, 1));
        AssertEnumValues(
            (CatalogConfigurationState.Declared, 0),
            (CatalogConfigurationState.Configured, 1),
            (CatalogConfigurationState.Disabled, 2));
        AssertEnumValues(
            (CatalogRecoverySemantics.ReplaySafe, 0),
            (CatalogRecoverySemantics.Idempotent, 1),
            (CatalogRecoverySemantics.Reconcileable, 2),
            (CatalogRecoverySemantics.NonRecoverable, 3));
        AssertEnumValues(
            (CatalogAvailabilityStatus.Unknown, 0),
            (CatalogAvailabilityStatus.Available, 1),
            (CatalogAvailabilityStatus.Degraded, 2),
            (CatalogAvailabilityStatus.Unavailable, 3));
        AssertEnumValues(
            (CatalogAvailabilityRequirement.Any, 0),
            (CatalogAvailabilityRequirement.CurrentlyAvailable, 1));
        AssertEnumValues(
            (DiscoveryExactMatchKind.None, 0),
            (DiscoveryExactMatchKind.NameOrAlias, 1),
            (DiscoveryExactMatchKind.OperationOrCapabilityId, 2),
            (DiscoveryExactMatchKind.DescriptorId, 3));
        AssertEnumValues(
            (DiscoveryCompatibilityEvidence.None, 0),
            (DiscoveryCompatibilityEvidence.Kind, 1 << 0),
            (DiscoveryCompatibilityEvidence.RequiredTag, 1 << 1),
            (DiscoveryCompatibilityEvidence.OperationOrCapability, 1 << 2),
            (DiscoveryCompatibilityEvidence.Signal, 1 << 3),
            (DiscoveryCompatibilityEvidence.InputSchema, 1 << 4),
            (DiscoveryCompatibilityEvidence.OutputSchema, 1 << 5),
            (DiscoveryCompatibilityEvidence.Lifecycle, 1 << 6),
            (DiscoveryCompatibilityEvidence.Invocability, 1 << 7),
            (DiscoveryCompatibilityEvidence.Configuration, 1 << 8));
        AssertEnumValues(
            (DiscoveryStatus.Ready, 0),
            (DiscoveryStatus.SemanticDegraded, 1),
            (DiscoveryStatus.Initializing, 2),
            (DiscoveryStatus.StaleCursor, 3));
        AssertEnumValues(
            (CatalogInspectionStatus.Found, 0),
            (CatalogInspectionStatus.StaleDescriptor, 1),
            (CatalogInspectionStatus.Retired, 2),
            (CatalogInspectionStatus.NotFound, 3));
        AssertEnumValues(
            (InspectionReferenceKind.CatalogDescriptor, 0),
            (InspectionReferenceKind.Neuron, 1),
            (InspectionReferenceKind.Synapse, 2),
            (InspectionReferenceKind.Entity, 3),
            (InspectionReferenceKind.DurableResource, 4));
        AssertEnumValues(
            (InspectionStatus.Found, 0),
            (InspectionStatus.StaleReference, 1),
            (InspectionStatus.Retired, 2),
            (InspectionStatus.NotFound, 3),
            (InspectionStatus.UnsupportedReference, 4));
        AssertEnumValues(
            (CatalogMutationKind.Upsert, 0),
            (CatalogMutationKind.Tombstone, 1));
    }

    private static string AliasOf(Type type)
    {
        var attribute = Assert.Single(type.GetCustomAttributesData(), static attribute =>
            attribute.AttributeType == typeof(AliasAttribute));
        return Assert.IsType<string>(Assert.Single(attribute.ConstructorArguments).Value);
    }

    private static void AssertEnumValues<TEnum>(params (TEnum Value, int Number)[] expected)
        where TEnum : struct, Enum
        => Assert.Equal(
            expected,
            Enum.GetValues<TEnum>().Select(static value => (value, Convert.ToInt32(value))).ToArray());

    private static string AliasOf(MemberInfo member)
    {
        var attribute = Assert.Single(member.GetCustomAttributesData(), static attribute =>
            attribute.AttributeType == typeof(AliasAttribute));
        return Assert.IsType<string>(Assert.Single(attribute.ConstructorArguments).Value);
    }

    private static void AssertIds<T>(params (string Name, int Id)[] expected)
    {
        var actual = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(static property => property.GetCustomAttribute<IdAttribute>() is not null)
            .ToDictionary(
                static property => property.Name,
                static property => Convert.ToInt32(
                    Assert.Single(
                        Assert.Single(
                            property.GetCustomAttributesData(),
                            static attribute => attribute.AttributeType == typeof(IdAttribute))
                        .ConstructorArguments)
                    .Value),
                StringComparer.Ordinal);

        Assert.Equal(
            expected.OrderBy(static item => item.Name),
            actual.Select(static item => (item.Key, item.Value)).OrderBy(static item => item.Key));
    }
}
