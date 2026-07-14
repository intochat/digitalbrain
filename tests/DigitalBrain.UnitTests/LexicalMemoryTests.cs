using System.Text.Json;
using DigitalBrain.Features.Sdk;
using DigitalBrain.Kernel.Contracts;
using DigitalBrain.Kernel.Memory;
using Xunit;

namespace DigitalBrain.UnitTests;

public sealed class LexicalMemoryTests
{
    private static readonly BrainOwnerId Owner = new("owner-1");
    private static readonly ActorId Actor = new("feature-1");

    [Fact]
    public async Task Recall_ranks_exact_tags_then_token_overlap_recency_and_fact_id()
    {
        var memory = Memory();
        await Remember(memory, "z-exact", "unrelated", ["Project"], 1);
        await Remember(memory, "token-new", "project alpha", ["other"], 4);
        await Remember(memory, "a-tie", "project alpha", ["project"], 2);
        await Remember(memory, "b-tie", "project alpha", ["PROJECT"], 2);

        var recalled = await memory.RecallAsync(
            Owner,
            Actor,
            new MemoryRecallRequest("PROJECT alpha", [" project "], 4),
            "correlation-recall");

        Assert.Equal(["a-tie", "b-tie", "z-exact", "token-new"], recalled.Select(fact => fact.FactId));
        Assert.All(recalled.Take(3), fact => Assert.Equal("project", Assert.Single(fact.Tags)));
    }

    [Fact]
    public async Task Remember_normalizes_tags_and_is_idempotent_for_the_same_fact()
    {
        var memory = Memory();
        var intent = new MemoryRememberIntent("remember-1", "fact-1", "text", [" Beta ", "alpha", "ALPHA"]);

        var first = await memory.RememberAsync(Owner, Actor, intent, "correlation-1", At(1));
        var duplicate = await memory.RememberAsync(Owner, Actor, intent, "correlation-2", At(2));
        var fact = await memory.InspectAsync(Owner, Actor, "fact-1", "correlation-inspect");

        Assert.Equal(MemoryWriteStatus.Created, first);
        Assert.Equal(MemoryWriteStatus.AlreadyPresent, duplicate);
        Assert.Equal(["alpha", "beta"], fact.Tags);
        Assert.Equal(At(1), fact.CreatedAt);
        Assert.Equal(At(1), fact.UpdatedAt);
    }

    [Fact]
    public async Task Capacity_is_exact_and_never_silently_evicts()
    {
        var memory = Memory();
        for (var index = 0; index < MemoryService.MaximumFactsPerOwner; index++)
            Assert.Equal(
                MemoryWriteStatus.Created,
                await Remember(memory, $"fact-{index:D4}", $"text-{index}", [], index));

        var rejected = await Remember(memory, "overflow", "overflow", [], 3_000);
        var exported = await memory.ExportAsync(Owner, Actor, "correlation-export");

        Assert.Equal(MemoryWriteStatus.CapacityReached, rejected);
        Assert.Equal(MemoryService.MaximumFactsPerOwner, exported.Count);
        Assert.DoesNotContain(exported, fact => fact.FactId == "overflow");
    }

    [Fact]
    public void Text_and_tag_limits_are_utf8_and_count_bounded()
    {
        _ = new MemoryRememberIntent("operation", "fact", new string('a', 2_048),
            Enumerable.Range(0, 16).Select(index => $"tag-{index}").ToArray());

        Assert.Throws<ArgumentException>(() =>
            new MemoryRememberIntent("operation", "fact", new string('a', 2_049), []));
        Assert.Throws<ArgumentException>(() =>
            new MemoryRememberIntent("operation", "fact", "text",
                Enumerable.Range(0, 17).Select(index => $"tag-{index}").ToArray()));
    }

    [Fact]
    public async Task Reserved_capacity_row_cannot_be_used_as_a_fact_id()
    {
        var memory = Memory();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            Remember(memory, "!capacity", "text", [], 1));
    }

    [Fact]
    public async Task Correct_requires_the_current_etag_and_forget_physically_deletes()
    {
        var memory = Memory();
        await Remember(memory, "fact-1", "before", ["old"], 1);
        var before = await memory.InspectAsync(Owner, Actor, "fact-1", "correlation-inspect");

        var corrected = await memory.CorrectAsync(
            Owner,
            new ActorId("owner-actor"),
            "fact-1",
            "after",
            ["new"],
            before.ETag,
            "correlation-correct",
            At(2));

        await Assert.ThrowsAsync<ArgumentException>(() => memory.CorrectAsync(
            Owner,
            new ActorId("owner-actor"),
            "fact-1",
            "unconditional",
            [],
            "*",
            "correlation-wildcard",
            At(3)));
        await Assert.ThrowsAsync<MemoryConflictException>(() => memory.CorrectAsync(
            Owner,
            new ActorId("owner-actor"),
            "fact-1",
            "stale",
            [],
            before.ETag,
            "correlation-stale",
            At(3)));
        Assert.True(await memory.ForgetAsync(
            Owner,
            new ActorId("owner-actor"),
            "fact-1",
            corrected.ETag,
            "correlation-forget"));
        await Assert.ThrowsAsync<MemoryNotFoundException>(() =>
            memory.InspectAsync(Owner, Actor, "fact-1", "correlation-missing"));
        Assert.Empty(await memory.ExportAsync(Owner, Actor, "correlation-export"));
    }

    [Fact]
    public async Task Audit_records_identifiers_and_outcomes_without_memory_payloads()
    {
        var audit = new RecordingAuditSink();
        var memory = Memory(audit);
        await memory.RememberAsync(
            Owner,
            Actor,
            new MemoryRememberIntent("remember-1", "fact-1", "secret text", ["secret-tag"]),
            "correlation-secret",
            At(1));

        var json = JsonSerializer.Serialize(Assert.Single(audit.Entries));

        Assert.Contains("fact-1", json, StringComparison.Ordinal);
        Assert.Contains("Created", json, StringComparison.Ordinal);
        Assert.DoesNotContain("secret text", json, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-tag", json, StringComparison.Ordinal);
    }

    private static MemoryService Memory(IMemoryAuditSink? audit = null) =>
        new(new InMemoryMemoryFactStore(), audit ?? new RecordingAuditSink());

    private static Task<MemoryWriteStatus> Remember(
        MemoryService memory,
        string factId,
        string text,
        IReadOnlyList<string> tags,
        int second) =>
        memory.RememberAsync(
            Owner,
            Actor,
            new MemoryRememberIntent($"remember-{factId}", factId, text, tags),
            $"correlation-{factId}",
            At(second));

    private static DateTimeOffset At(int second) => DateTimeOffset.UnixEpoch.AddSeconds(second);

    private sealed class RecordingAuditSink : IMemoryAuditSink
    {
        public List<MemoryAuditRecord> Entries { get; } = [];

        public ValueTask WriteAsync(MemoryAuditRecord record, CancellationToken cancellationToken = default)
        {
            Entries.Add(record);
            return ValueTask.CompletedTask;
        }
    }
}
