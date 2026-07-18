using Ino.Core;
using Ino.Core.Hosting;
using Ino.Kernel.Contracts;
using Ino.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ino.Kernel.Tests;

/// <summary>
/// Phase 4 epilogue Slice 3A: <see cref="ProposalLog"/> grain tracks the
/// Pending → Approved / Rejected lifecycle. Pending entries are created via
/// <see cref="ProposalLog.RecordPendingAsync"/> (called by
/// <see cref="INeuronRegistry.StashDraftAsync"/>). Transitions to
/// Approved / Rejected are driven by the <see cref="ProposalDecided"/>
/// reactor. Tests are direct-object style (no Orleans cluster) because
/// ProposalLog is a pure state-machine with no grain dependencies.
/// </summary>
public sealed class ProposalLogTests
{
    static NeuronContext Ctx() => NeuronContextForTest.Create(
        source: new Caller.FromDomain(DomainId.From("kernel")));

    static ProposalEntry MakePending(string id, string userId = "u1", string cluster = "test-cluster") =>
        new(id, userId, cluster, "example prompt", [cluster], 3, DateTimeOffset.UtcNow,
            ProposalStatus.Pending, null, null, null);

    [Fact]
    public async Task RecordPending_creates_pending_entry()
    {
        var log = new ProposalLog(NullLogger<ProposalLog>.Instance);
        await log.RecordPendingAsync(MakePending("p1"));

        var list = await log.ListAsync(ProposalStatus.Pending, 0, 100);
        Assert.Single(list, e => e.ProposalId == "p1" && e.Status == ProposalStatus.Pending);
    }

    [Fact]
    public async Task ProposalDecided_Approve_flips_to_approved()
    {
        var log = new ProposalLog(NullLogger<ProposalLog>.Instance);
        await log.RecordPendingAsync(MakePending("p2"));

        await log.ReactAsync(
            new ProposalDecided("p2", ProposalStatus.Approved, "admin", DateTimeOffset.UtcNow),
            Ctx(), CancellationToken.None);

        var entry = await log.GetAsync("p2");
        Assert.NotNull(entry);
        Assert.Equal(ProposalStatus.Approved, entry.Status);
        Assert.Equal("admin", entry.DecidedBy);
    }

    [Fact]
    public async Task ProposalDecided_Reject_flips_to_rejected()
    {
        var log = new ProposalLog(NullLogger<ProposalLog>.Instance);
        await log.RecordPendingAsync(MakePending("p3"));

        await log.ReactAsync(
            new ProposalDecided("p3", ProposalStatus.Rejected, "admin", DateTimeOffset.UtcNow),
            Ctx(), CancellationToken.None);

        var entry = await log.GetAsync("p3");
        Assert.NotNull(entry);
        Assert.Equal(ProposalStatus.Rejected, entry.Status);
        Assert.Equal("admin", entry.DecidedBy);
    }

    [Fact]
    public async Task List_filters_by_status()
    {
        var log = new ProposalLog(NullLogger<ProposalLog>.Instance);
        var ctx = Ctx();

        await log.RecordPendingAsync(MakePending("pa"));
        await log.RecordPendingAsync(MakePending("pb"));

        await log.ReactAsync(
            new ProposalDecided("pa", ProposalStatus.Rejected, "admin", DateTimeOffset.UtcNow),
            ctx, CancellationToken.None);

        var pending = await log.ListAsync(ProposalStatus.Pending, 0, 100);
        Assert.Single(pending);
        Assert.Equal("pb", pending[0].ProposalId);

        var rejected = await log.ListAsync(ProposalStatus.Rejected, 0, 100);
        Assert.Single(rejected);
        Assert.Equal("pa", rejected[0].ProposalId);
    }
}
