using System.Security.Cryptography;
using System.Text.Json;
using DigitalBrain.Core.Runtime;
using RuntimeRequestContext = DigitalBrain.Core.Runtime.RequestContext;

namespace DigitalBrain.Tests.Runtime;

public sealed class JournalTailRecoveryTests
{
    [Fact]
    public void Feed_reopens_after_quarantining_only_an_incomplete_final_append()
    {
        var root = Path.Combine(Path.GetTempPath(), "digitalbrain-feed-tail-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "feed.jsonl");
        var integrityKey = RandomNumberGenerator.GetBytes(32);
        var context = new RuntimeRequestContext(
            new TenantId("tenant"),
            new WorkspaceId("workspace"),
            new PrincipalRef("user", PrincipalKind.User),
            "session",
            AuthAssurance.Password,
            "correlation",
            null,
            new HashSet<string>(StringComparer.Ordinal) { "brain.read" });
        try
        {
            var store = new PrivateFeedStore(path, integrityKey: integrityKey);
            store.Append(
                context,
                "surface",
                1,
                "compute-content-hash",
                JsonSerializer.SerializeToElement(new { kind = "safe" }));
            File.AppendAllText(path, "{\"Kind\":");

            var reopened = new PrivateFeedStore(path, integrityKey: integrityKey);

            Assert.Single(reopened.CatchUp(context, 0).Items);
            Assert.True(File.Exists(path + ".quarantine"));
            Assert.EndsWith(Environment.NewLine, File.ReadAllText(path), StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Feed_tail_repair_waits_for_the_shared_cross_instance_writer_exclusion()
    {
        var root = Path.Combine(Path.GetTempPath(), "digitalbrain-feed-tail-lock-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "feed.jsonl");
        var integrityKey = RandomNumberGenerator.GetBytes(32);
        var context = new RuntimeRequestContext(
            new TenantId("tenant"),
            new WorkspaceId("workspace"),
            new PrincipalRef("user", PrincipalKind.User),
            "session",
            AuthAssurance.Password,
            "correlation",
            null,
            new HashSet<string>(StringComparer.Ordinal) { "brain.read" });
        try
        {
            var store = new PrivateFeedStore(path, integrityKey: integrityKey);
            store.Append(
                context,
                "surface",
                1,
                "compute-content-hash",
                JsonSerializer.SerializeToElement(new { kind = "safe" }));
            File.AppendAllText(path, "{\"Kind\":");

            using var exclusion = JsonLinesJournalFile.AcquireWriterExclusion(path);
            var reopen = Task.Run(() => new PrivateFeedStore(path, integrityKey: integrityKey));
            await Task.Delay(100);
            Assert.False(reopen.IsCompleted);
            exclusion.Dispose();

            var reopened = await reopen;
            Assert.Single(reopened.CatchUp(context, 0).Items);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
