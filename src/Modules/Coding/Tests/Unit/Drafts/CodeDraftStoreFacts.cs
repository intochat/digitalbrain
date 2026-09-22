using DigitalBrain.Coding;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class CodeDraftStoreFacts
{
    [Fact]
    public async Task EnumerationAndAtomicReplacementCanOverlap()
    {
        var root = Path.Combine(Path.GetTempPath(), "brain-draft-tests", Guid.NewGuid().ToString("N"));
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var store = new DurableDocumentStore<CodeDraftDocument>(root, () => new());
            await store.UpdateAsync("draft", d => d.Id = "draft", ct);
            await Task.WhenAll(
                Task.Run(async () => { for (var i = 0; i < 100; i++) { Assert.Equal("draft", Assert.Single(await store.ListIdsAsync(ct))); } }, ct),
                Task.Run(async () => { for (var i = 0; i < 100; i++) { await store.UpdateAsync("draft", d => d.Id = "draft", ct); } }, ct));
        }
        finally { if (Directory.Exists(root)) { Directory.Delete(root, true); } }
    }

    [Fact]
    public async Task ChecksPinInputsAndRecoveryInterruptsOnlyPendingChecks()
    {
        var root = Path.Combine(Path.GetTempPath(), "brain-draft-tests", Guid.NewGuid().ToString("N"));
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var store = new CodeDraftStore(root);
            await store.SaveAsync("d", new(0, Guid.NewGuid(), "original", "tests", []), ct);
            var first = await store.QueueAsync("d", new(1, Guid.NewGuid()), ct);
            var second = await store.QueueAsync("d", new(1, Guid.NewGuid()), ct);
            await store.UpdateCheckAsync("d", first.OperationId, c => c with { Status = CodeCheckStatus.Cancelled }, ct);
            await store.SaveAsync("d", new(1, Guid.NewGuid(), "edited", "tests", []), ct);
            Assert.Equal("original", (await store.ReadInputAsync("d", second.OperationId, ct)).Source);
            var reopened = new CodeDraftStore(root);
            await reopened.InterruptPendingAsync(ct);
            Assert.Equal(CodeCheckStatus.Cancelled, (await reopened.ReadCheckAsync("d", first.OperationId, ct)).Status);
            Assert.Equal(CodeCheckStatus.Interrupted, (await reopened.ReadCheckAsync("d", second.OperationId, ct)).Status);
        }
        finally { if (Directory.Exists(root)) { Directory.Delete(root, true); } }
    }

    [Fact]
    public async Task SaveIsDurableAndIdempotent()
    {
        var root = Path.Combine(Path.GetTempPath(), "brain-draft-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var request = new SaveCodeDraft(0, Guid.NewGuid(), "Console.WriteLine(1);", "public class Tests { }", []);
            var store = new CodeDraftStore(root);
            var first = await store.SaveAsync("workspace/draft", request, TestContext.Current.CancellationToken);
            var reopened = new CodeDraftStore(root);
            var replay = await reopened.SaveAsync("workspace/draft", request, TestContext.Current.CancellationToken);
            Assert.Equal(first.Revision, replay.Revision);
            Assert.Equal(first.Source, (await reopened.ReadAsync("workspace/draft", TestContext.Current.CancellationToken)).Source);
            await Assert.ThrowsAsync<InvalidOperationException>(() => reopened.SaveAsync("workspace/draft", request with { Source = "changed" }, TestContext.Current.CancellationToken));
        }
        finally { if (Directory.Exists(root)) { Directory.Delete(root, true); } }
    }

    [Fact]
    public async Task ConcurrentSavesCannotBothWin()
    {
        var root = Path.Combine(Path.GetTempPath(), "brain-draft-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new CodeDraftStore(root);
            var first = new SaveCodeDraft(0, Guid.NewGuid(), "Console.WriteLine(1);", "public class Tests { }", []);
            var tasks = new[] { store.SaveAsync("draft", first, TestContext.Current.CancellationToken), store.SaveAsync("draft", first with { OperationId = Guid.NewGuid() }, TestContext.Current.CancellationToken) };
            try { await Task.WhenAll(tasks); } catch (InvalidOperationException) { }
            _ = Assert.Single(tasks, t => t.IsCompletedSuccessfully);
        }
        finally { if (Directory.Exists(root)) { Directory.Delete(root, true); } }
    }
}
