using System.Text;
using DigitalBrain.Kernel.Contracts;
using DigitalBrain.Kernel.Features;

namespace DigitalBrain.OrleansTests.Features;

public sealed class FeatureApprovalLedgerTests
{
    private static readonly ActorId Actor = new("actor-approval-ledger");
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Compaction_strips_source_only_from_superseded_records()
    {
        var historical = Approval(1, FeatureApprovalStatus.Superseded, sourceBytes: 1024);
        var current = Approval(2, FeatureApprovalStatus.Pending, sourceBytes: 1024);

        var compacted = FeatureApprovalLedger.Compact([historical, current]);

        Assert.Equal([historical.ApprovalId, current.ApprovalId], compacted.Select(approval => approval.ApprovalId));
        Assert.Null(compacted[0].Release.Source);
        Assert.NotNull(compacted[1].Release.Source);
        Assert.Equal(historical.Release.SourceReference, compacted[0].Release.SourceReference);
        Assert.Equal(historical.Release.RequestedCapabilities, compacted[0].Release.RequestedCapabilities);
        Assert.Equal(historical.Grants, compacted[0].Grants);
        Assert.Equal(historical.DecisionId, compacted[0].DecisionId);
        Assert.Equal(historical.DecisionActorId, compacted[0].DecisionActorId);
    }

    [Fact]
    public void Compaction_retains_the_newest_history_within_the_total_record_target()
    {
        var history = Enumerable.Range(1, 70)
            .Select(revision => Approval(revision, FeatureApprovalStatus.Superseded))
            .ToArray();
        var current = Approval(71, FeatureApprovalStatus.Pending);

        var compacted = FeatureApprovalLedger.Compact([.. history, current]);

        Assert.Equal(FeatureLimits.ApprovalLedgerRecords, compacted.Length);
        Assert.Same(current, compacted[^1]);
        Assert.Equal(
            Enumerable.Range(8, 63),
            compacted.Where(approval => approval.Status == FeatureApprovalStatus.Superseded).Select(approval => checked((int)approval.Revision)));
    }

    [Fact]
    public void Compaction_stops_at_the_first_newest_record_that_exceeds_the_byte_target()
    {
        var olderSmall = Approval(100, FeatureApprovalStatus.Superseded);
        var newestLarge = Approval(200, FeatureApprovalStatus.Superseded, grants: LargeGrants("newest"));
        var currentLarge = Approval(300, FeatureApprovalStatus.Pending, grants: LargeGrants("current"));
        var compactedNewest = newestLarge with { Release = newestLarge.Release with { Source = null } };
        var compactedOlder = olderSmall with { Release = olderSmall.Release with { Source = null } };

        Assert.True(FeatureApprovalLedger.SerializedBytes([currentLarge]) < FeatureLimits.ApprovalLedgerUtf8Bytes);
        Assert.True(FeatureApprovalLedger.SerializedBytes([currentLarge, compactedOlder]) < FeatureLimits.ApprovalLedgerUtf8Bytes);
        Assert.True(FeatureApprovalLedger.SerializedBytes([currentLarge, compactedNewest]) > FeatureLimits.ApprovalLedgerUtf8Bytes);

        var compacted = FeatureApprovalLedger.Compact([olderSmall, newestLarge, currentLarge]);

        Assert.Single(compacted);
        Assert.Same(currentLarge, compacted[0]);
    }

    [Fact]
    public void Sixty_five_mandatory_current_records_soft_overflow_count_and_evict_all_history()
    {
        var history = Approval(1, FeatureApprovalStatus.Superseded);
        var current = Enumerable.Range(2, 65)
            .Select(revision => Approval(revision, FeatureApprovalStatus.Pending))
            .ToArray();

        var compacted = FeatureApprovalLedger.Compact([history, .. current]);

        Assert.Equal(65, compacted.Length);
        Assert.All(compacted, approval => Assert.NotEqual(FeatureApprovalStatus.Superseded, approval.Status));
        Assert.Equal(current.Select(approval => approval.ApprovalId), compacted.Select(approval => approval.ApprovalId));
    }

    [Fact]
    public void Mandatory_current_bytes_soft_overflow_and_evict_all_history()
    {
        var history = Approval(1, FeatureApprovalStatus.Superseded);
        var current = Approval(
            2,
            FeatureApprovalStatus.Pending,
            sourceBytes: FeatureLimits.ApprovalLedgerUtf8Bytes);

        var compacted = FeatureApprovalLedger.Compact([history, current]);

        Assert.Single(compacted);
        Assert.Same(current, compacted[0]);
        Assert.NotNull(compacted[0].Release.Source);
        Assert.True(FeatureApprovalLedger.SerializedBytes(compacted) > FeatureLimits.ApprovalLedgerUtf8Bytes);
    }

    [Fact]
    public void Large_source_is_stripped_while_large_grants_count_toward_the_budget()
    {
        var olderLarge = Approval(1, FeatureApprovalStatus.Superseded, grants: LargeGrants("older"));
        var newestLarge = Approval(
            2,
            FeatureApprovalStatus.Superseded,
            grants: LargeGrants("newer"),
            sourceBytes: 3 * 1024 * 1024);
        var current = Approval(3, FeatureApprovalStatus.Pending);

        var compacted = FeatureApprovalLedger.Compact([olderLarge, newestLarge, current]);

        Assert.Equal([newestLarge.ApprovalId, current.ApprovalId], compacted.Select(approval => approval.ApprovalId));
        Assert.Null(compacted[0].Release.Source);
        Assert.Equal(32, compacted[0].Grants.Length);
        Assert.True(FeatureApprovalLedger.SerializedBytes(compacted) <= FeatureLimits.ApprovalLedgerUtf8Bytes);
    }

    [Fact]
    public void Decision_rejects_oversized_and_control_character_identifiers_without_mutation()
    {
        var proposal = Proposal(900);
        var proposed = FeatureHubTransitions.Propose(FeatureHubState.Empty, proposal, 0, Now);
        var approval = Assert.Single(proposed.Approvals);

        foreach (var invalid in new[] { new string('d', 257), "decision\ncontrol" })
        {
            Assert.Throws<ArgumentException>(() => FeatureHubTransitions.Decide(
                proposed,
                new FeatureApprovalDecision(approval.ApprovalId, proposal.Release.Digest, true, invalid, Actor),
                proposed.Revision,
                Now.AddMinutes(1)));
        }

        Assert.Same(approval, Assert.Single(proposed.Approvals));
        Assert.Equal(1, proposed.Revision);
    }

    [Fact]
    public void Propose_compacts_existing_history_after_appending_the_current_record()
    {
        var history = Enumerable.Range(1, 64)
            .Select(revision => Approval(revision, FeatureApprovalStatus.Superseded, sourceBytes: 64))
            .ToArray();
        var state = FeatureHubState.Empty with { Approvals = history, Revision = 1000 };
        var proposal = Proposal(1001);

        var proposed = FeatureHubTransitions.Propose(state, proposal, state.Revision, Now);

        Assert.Equal(FeatureLimits.ApprovalLedgerRecords, proposed.Approvals.Length);
        Assert.Contains(proposed.Approvals, approval =>
            approval.Status == FeatureApprovalStatus.Pending &&
            approval.InstallationId == proposal.InstallationId);
        Assert.DoesNotContain(proposed.Approvals, approval => approval.ApprovalId == history[0].ApprovalId);
        Assert.All(
            proposed.Approvals.Where(approval => approval.Status == FeatureApprovalStatus.Superseded),
            approval => Assert.Null(approval.Release.Source));
    }

    [Fact]
    public void Decide_compacts_history_after_growing_the_current_decision_record()
    {
        var history = Enumerable.Range(1, 63)
            .Select(revision => Approval(revision, FeatureApprovalStatus.Superseded, sourceBytes: 64))
            .ToArray();
        var pending = Approval(1000, FeatureApprovalStatus.Pending);
        var state = FeatureHubState.Empty with
        {
            Approvals = [.. history, pending],
            Releases = [pending.Release],
            Revision = 1000
        };

        var decided = FeatureHubTransitions.Decide(
            state,
            new FeatureApprovalDecision(
                pending.ApprovalId,
                pending.Release.Digest,
                true,
                new string('d', 256),
                Actor),
            state.Revision,
            Now);

        Assert.Equal(FeatureLimits.ApprovalLedgerRecords, decided.Approvals.Length);
        Assert.All(
            decided.Approvals.Where(approval => approval.Status == FeatureApprovalStatus.Superseded),
            approval => Assert.Null(approval.Release.Source));
        var current = decided.Approvals.Single(approval => approval.ApprovalId == pending.ApprovalId);
        Assert.Equal(FeatureApprovalStatus.Approved, current.Status);
        Assert.Equal(new string('d', 256), current.DecisionId);
    }

    [Fact]
    public void Serialized_accounting_uses_exact_utf8_for_every_variable_persisted_field()
    {
        var baseline = AccountingApproval();
        var source = baseline.Release.Source!;
        var file = Assert.Single(source.Files);
        var grant = Assert.Single(baseline.Grants);
        var variants = new Dictionary<string, FeatureApprovalState>
        {
            [nameof(baseline.ApprovalId)] = baseline with { ApprovalId = baseline.ApprovalId + "é" },
            [nameof(baseline.InstallationId)] = baseline with
            {
                InstallationId = new FeatureInstallationId(baseline.InstallationId.Value + "é")
            },
            [nameof(baseline.Release.SourceReference)] = baseline with
            {
                Release = baseline.Release with { SourceReference = baseline.Release.SourceReference + "é" }
            },
            [nameof(baseline.Release.RequestedCapabilities)] = baseline with
            {
                Release = baseline.Release with
                {
                    RequestedCapabilities = [baseline.Release.RequestedCapabilities[0] + "é"]
                }
            },
            [nameof(baseline.Release.Dependencies)] = baseline with
            {
                Release = baseline.Release with { Dependencies = [baseline.Release.Dependencies[0] + "é"] }
            },
            [nameof(source.ImplementationProjectPath)] = baseline with
            {
                Release = baseline.Release with
                {
                    Source = source with { ImplementationProjectPath = source.ImplementationProjectPath + "é" }
                }
            },
            [nameof(source.ScenarioProjectPath)] = baseline with
            {
                Release = baseline.Release with
                {
                    Source = source with { ScenarioProjectPath = source.ScenarioProjectPath + "é" }
                }
            },
            [nameof(file.Path)] = baseline with
            {
                Release = baseline.Release with
                {
                    Source = source with { Files = [file with { Path = file.Path + "é" }] }
                }
            },
            [nameof(file.Content)] = baseline with
            {
                Release = baseline.Release with
                {
                    Source = source with { Files = [file with { Content = file.Content + "é" }] }
                }
            },
            [nameof(baseline.AddedCapabilities)] = baseline with
            {
                AddedCapabilities = [baseline.AddedCapabilities[0] + "é"]
            },
            [nameof(baseline.RemovedCapabilities)] = baseline with
            {
                RemovedCapabilities = [baseline.RemovedCapabilities[0] + "é"]
            },
            [nameof(baseline.DecisionId)] = baseline with { DecisionId = baseline.DecisionId + "é" },
            [nameof(grant.CapabilityId)] = baseline with
            {
                Grants = [grant with { CapabilityId = grant.CapabilityId + "é" }]
            },
            [nameof(grant.ProviderConnectionId)] = baseline with
            {
                Grants = [grant with
                {
                    ProviderConnectionId = new ProviderConnectionId(grant.ProviderConnectionId!.Value.Value + "é")
                }]
            },
            [nameof(grant.ConstraintsJson)] = baseline with
            {
                Grants = [grant with { ConstraintsJson = grant.ConstraintsJson + "é" }]
            },
            [nameof(grant.Provider)] = baseline with
            {
                Grants = [grant with { Provider = grant.Provider + "é" }]
            },
            [nameof(baseline.DecisionActorId)] = baseline with
            {
                DecisionActorId = new ActorId(baseline.DecisionActorId!.Value.Value + "é")
            }
        };
        var baselineBytes = FeatureApprovalLedger.SerializedBytes([baseline]);

        foreach (var (field, variant) in variants)
        {
            var delta = FeatureApprovalLedger.SerializedBytes([variant]) - baselineBytes;
            Assert.True(delta == Encoding.UTF8.GetByteCount("é"), $"{field} changed the ledger by {delta} bytes.");
        }
    }

    [Fact]
    public void Serialized_accounting_includes_fixed_structural_overhead()
    {
        var approval = AccountingApproval();
        var rawStringBytes = PersistedStrings(approval).Sum(Encoding.UTF8.GetByteCount);

        Assert.True(FeatureApprovalLedger.SerializedBytes([approval]) > rawStringBytes);
    }

    [Fact]
    public void Serialized_accounting_checks_integer_overflow()
    {
        Assert.Throws<OverflowException>(() => FeatureApprovalLedger.CheckedAdd(int.MaxValue, 1));
        Assert.Equal(FeatureLimits.ApprovalLedgerUtf8Bytes, FeatureApprovalLedger.CheckedAdd(
            FeatureLimits.ApprovalLedgerUtf8Bytes - 1,
            1));
    }

    private static FeatureApprovalState Approval(
        int revision,
        FeatureApprovalStatus status,
        FeatureGrantState[]? grants = null,
        int sourceBytes = 0)
    {
        grants ??= [];
        var release = Release(revision, grants, sourceBytes);
        var decided = status is FeatureApprovalStatus.Approved or FeatureApprovalStatus.Rejected or FeatureApprovalStatus.Superseded;
        return new FeatureApprovalState(
            $"approval-{revision:D4}",
            new FeatureInstallationId($"installation-{revision:D4}"),
            release,
            grants.Select(grant => grant.CapabilityId).ToArray(),
            [],
            status,
            decided ? $"decision-{revision:D4}" : null,
            decided ? Now.AddMinutes(revision) : null,
            revision,
            grants,
            decided ? new ActorId($"actor-{revision:D4}") : null);
    }

    private static FeatureApprovalState AccountingApproval()
    {
        var grant = new FeatureGrantState(
            "capability.accounting",
            7,
            new ProviderConnectionId("connection-accounting"),
            "{\"allowedToolIds\":[\"capability.accounting\"]}",
            "sandbox");
        var approval = Approval(700, FeatureApprovalStatus.Approved, [grant], sourceBytes: 16);
        return approval with { RemovedCapabilities = ["capability.removed"] };
    }

    private static IEnumerable<string> PersistedStrings(FeatureApprovalState approval)
    {
        yield return approval.ApprovalId;
        yield return approval.InstallationId.Value;
        yield return approval.Release.Digest.Value;
        yield return approval.Release.SourceReference;
        foreach (var value in approval.Release.RequestedCapabilities) yield return value;
        foreach (var value in approval.Release.Dependencies) yield return value;
        if (approval.Release.Source is { } source)
        {
            yield return source.ImplementationProjectPath;
            yield return source.ScenarioProjectPath;
            foreach (var file in source.Files)
            {
                yield return file.Path;
                yield return file.Content;
            }
        }
        foreach (var value in approval.AddedCapabilities) yield return value;
        foreach (var value in approval.RemovedCapabilities) yield return value;
        if (approval.DecisionId is not null) yield return approval.DecisionId;
        foreach (var grant in approval.Grants)
        {
            yield return grant.CapabilityId;
            if (grant.ProviderConnectionId is { } connection) yield return connection.Value;
            yield return grant.ConstraintsJson;
            if (grant.Provider is not null) yield return grant.Provider;
        }
        if (approval.DecisionActorId is { } actor) yield return actor.Value;
    }

    private static FeatureReleaseProposal Proposal(int revision)
    {
        var release = Release(revision, [], 0);
        return new FeatureReleaseProposal(new FeatureInstallationId($"installation-{revision:D4}"), release, []);
    }

    private static FeatureReleaseMetadata Release(int revision, FeatureGrantState[] grants, int sourceBytes)
    {
        var digest = new ReleaseDigest(revision.ToString("x64"));
        var source = sourceBytes == 0
            ? null
            : new FeatureSourceSnapshot(
                $"src/{revision:D4}/Feature.csproj",
                $"tests/{revision:D4}/Feature.Scenarios.csproj",
                [new FeatureSourceFile($"src/{revision:D4}/Feature.cs", new string('s', sourceBytes))]);
        return new FeatureReleaseMetadata(
            digest,
            "sha256:" + digest.Value,
            FeatureSourceKind.RuntimeAuthored,
            grants.Select(grant => grant.CapabilityId).ToArray(),
            ["DigitalBrain.Features.Sdk"],
            source);
    }

    private static FeatureGrantState[] LargeGrants(string suffix) => Enumerable.Range(0, 32)
        .Select(index =>
        {
            var capabilityId = $"capability.{suffix}.{index:D2}";
            return new FeatureGrantState(
                capabilityId,
                1,
                null,
                LargeConstraints(capabilityId),
                "sandbox");
        })
        .ToArray();

    private static string LargeConstraints(string capabilityId)
    {
        var prefix = $"{{\"allowedToolIds\":[\"{capabilityId}\"],\"padding\":\"";
        const string suffix = "\"}";
        return prefix + new string('x', 65_536 - Encoding.UTF8.GetByteCount(prefix) - Encoding.UTF8.GetByteCount(suffix)) + suffix;
    }
}
