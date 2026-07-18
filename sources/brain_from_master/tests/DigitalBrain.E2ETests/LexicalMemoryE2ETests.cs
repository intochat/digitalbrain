using System.Text.Json;
using DigitalBrain.Kernel.Capabilities;
using DigitalBrain.Kernel.Contracts;
using DigitalBrain.Kernel.Memory;
using Xunit;

namespace DigitalBrain.E2ETests;

public sealed class LexicalMemoryE2ETests
{
    [Fact]
    public async Task Remember_recall_owner_correction_export_and_forget_complete_the_memory_lifecycle()
    {
        var audit = new RecordingAudit();
        var memory = new MemoryService(new InMemoryMemoryFactStore(), audit);
        var remember = new MemoryRememberCapabilityHandler(memory, TimeProvider.System);
        var recall = new MemoryRecallCapabilityHandler(memory);
        var owner = new BrainOwnerId("owner-e2e");
        var actor = new ActorId("feature-e2e");

        await remember.ExecuteAsync(Request(owner, actor, "memory.remember", new
        {
            factId = "release-date",
            text = "Project Alpha ships Friday",
            tags = new[] { "Project" }
        }), Grant(owner, "memory.remember"));
        var recalled = await recall.ExecuteAsync(Request(owner, actor, "memory.recall", new
        {
            query = "ships",
            tags = new[] { "project" },
            limit = 20
        }), Grant(owner, "memory.recall"));
        var before = await memory.InspectAsync(owner, actor, "release-date", "inspect-e2e");
        var corrected = await memory.CorrectAsync(
            owner,
            actor,
            "release-date",
            "Project Alpha ships Monday",
            ["project", "schedule"],
            before.ETag,
            "correct-e2e",
            DateTimeOffset.UtcNow);
        var exported = await memory.ExportAsync(owner, actor, "export-e2e");
        var deleted = await memory.ForgetAsync(
            owner,
            actor,
            "release-date",
            corrected.ETag,
            "forget-e2e");

        Assert.Single(recalled.GetProperty("facts").EnumerateArray());
        Assert.Equal("Project Alpha ships Monday", Assert.Single(exported).Text);
        Assert.True(deleted);
        Assert.Empty(await memory.ExportAsync(owner, actor, "empty-e2e"));
        Assert.All(audit.Records, record =>
        {
            var serialized = JsonSerializer.Serialize(record);
            Assert.DoesNotContain("Project Alpha", serialized, StringComparison.Ordinal);
            Assert.DoesNotContain("schedule", serialized, StringComparison.Ordinal);
        });
    }

    private static CapabilityRequest Request(
        BrainOwnerId owner,
        ActorId actor,
        string capabilityId,
        object payload) =>
        new(
            owner,
            actor,
            new FeatureInstallationId("installation-e2e"),
            new ReleaseDigest(new string('b', 64)),
            "input-e2e",
            "operation-e2e",
            capabilityId,
            1,
            null,
            new GrantRevision(1),
            JsonSerializer.SerializeToElement(payload),
            DateTimeOffset.UtcNow.AddMinutes(1),
            "correlation-e2e",
            null);

    private static CapabilityGrant Grant(BrainOwnerId owner, string capabilityId) =>
        new(
            owner,
            new FeatureInstallationId("installation-e2e"),
            new ReleaseDigest(new string('b', 64)),
            capabilityId,
            1,
            null,
            new GrantRevision(1),
            JsonSerializer.SerializeToElement(new { allowedToolIds = new[] { capabilityId } }),
            true,
            false);

    private sealed class RecordingAudit : IMemoryAuditSink
    {
        public List<MemoryAuditRecord> Records { get; } = [];

        public ValueTask WriteAsync(MemoryAuditRecord record, CancellationToken cancellationToken = default)
        {
            Records.Add(record);
            return ValueTask.CompletedTask;
        }
    }
}
