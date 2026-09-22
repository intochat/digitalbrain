using IntoChat;
using Xunit;
using DigitalBrain.Coding;
using DigitalBrain.Contracts;
using DigitalBrain.Behavior;
using Microsoft.Extensions.Options;

namespace IntoChat.Tests;

public sealed class BehaviorCatalogFacts
{
    [Fact]
    public async Task ActivityShowsTheLatestBoundedLogPage()
    {
        var calls = new List<(long After, int Limit)>();
        var program = System.Reflection.DispatchProxy.Create<IBehaviorProgram, BehaviorAuthoringFacts.MethodProxy>();
        ((BehaviorAuthoringFacts.MethodProxy)(object)program).Handler = (_, args) =>
        {
            var after = (long)args[0]!;
            var limit = (int)args[1]!;
            calls.Add((after, limit));
            return Task.FromResult(new BehaviorLogPage([], 220, false));
        };
        var page = await BehaviorManagement.ReadRecentLogs(program, TestContext.Current.CancellationToken);
        Assert.Equal([(0L, 1), (70L, 150)], calls);
        Assert.True(page.Truncated);
        Assert.Equal(220, page.LastSequence);
    }

    [Fact]
    public async Task CheckedSourceMustMatchTheExactRevisionAndHash()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new BehaviorCatalogStore(Path.Combine(Path.GetTempPath(), "behavior-catalog-tests", Guid.NewGuid().ToString("N")));
        await store.Register("scope", "timer", ct);
        var operation = Guid.NewGuid();
        var draft = new CodeDraftSnapshot(2, "source", "tests", [], operation);
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(draft.Source)));
        var check = new CodeCheckSnapshot(operation, 2, CodeCheckStatus.Passed, [], null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, new("artifact", hash, "env"));
        await store.RememberChecked("scope", "timer", draft with { Source = "changed" }, check, ct);
        Assert.Null(await store.CheckedSource("scope", "timer", "artifact", ct));
        await store.RememberChecked("scope", "timer", draft, check with { Revision = 1 }, ct);
        Assert.Null(await store.CheckedSource("scope", "timer", "artifact", ct));
        await store.RememberChecked("scope", "timer", draft, check, ct);
        Assert.Equal("source", (await store.CheckedSource("scope", "timer", "artifact", ct))?.Source);
    }
    [Fact]
    public void OnlyTheCurrentPassingCheckCanBeDeployed()
    {
        var operation = Guid.NewGuid();
        var draft = new CodeDraftSnapshot(2, "source", "tests", [], operation);
        var check = new CodeCheckSnapshot(operation, 2, CodeCheckStatus.Passed, [], new(1, 1, 0, 0), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, new("id", "source", "env"));
        Assert.True(BehaviorManagement.CanDeploy(draft, check));
        Assert.False(BehaviorManagement.CanDeploy(draft with { Revision = 3 }, check));
        Assert.False(BehaviorManagement.CanDeploy(draft, check with { OperationId = Guid.NewGuid() }));
        Assert.False(BehaviorManagement.CanDeploy(draft, check with { Status = CodeCheckStatus.Failed }));
        Assert.False(BehaviorManagement.CanDeploy(draft, check with { Artifact = null }));
    }

    [Fact]
    public async Task DisabledRuntimeStillShowsCatalogAndDraftIdentity()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new BehaviorCatalogStore(Path.Combine(Path.GetTempPath(), "behavior-catalog-tests", Guid.NewGuid().ToString("N")));
        await store.Register("scope", "timer", ct);
        var manager = new BehaviorManagement(null!, store, Options.Create(new BehaviorAuthoringOptions()), Options.Create(new CodeExecutionOptions()), Options.Create(new BehaviorOptions()));
        var detail = await manager.Detail("scope", "timer", ct);
        Assert.Equal("timer", detail.Description.Id);
        Assert.False(detail.AllowActivation);
        Assert.False(detail.AllowValidation);
        Assert.False(detail.CanDeploy);
    }
    [Fact]
    public async Task CatalogPersistsSeparatesWorkspacesAndPreservesNamesOnRepeatedRegistration()
    {
        var root = Path.Combine(Path.GetTempPath(), "behavior-catalog-tests", Guid.NewGuid().ToString("N"));
        var ct = TestContext.Current.CancellationToken;
        var store = new BehaviorCatalogStore(root);
        await store.Register("one", "timer", ct);
        await store.Describe("one", "timer", new(0, "Timer status", "Show ticks", ["Timer tick"], ["Update text"]), ct);
        await store.Register("one", "timer", ct);
        var restarted = new BehaviorCatalogStore(root);
        Assert.Equal("Timer status", Assert.Single(await restarted.List("one", ct)).Name);
        Assert.Empty(await restarted.List("two", ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => restarted.Describe("one", "timer", new(0, "Old", "", [], []), ct));
    }

    [Fact]
    public async Task CatalogRejectsInvalidIdsAndOversizedDescriptions()
    {
        var store = new BehaviorCatalogStore(Path.Combine(Path.GetTempPath(), "behavior-catalog-tests", Guid.NewGuid().ToString("N")));
        var ct = TestContext.Current.CancellationToken;
        await Assert.ThrowsAsync<ArgumentException>(() => store.Register("one", "../timer", ct));
        await Assert.ThrowsAsync<ArgumentException>(() => store.Describe("one", "timer", new(0, "Name", new string('x', 4001), [], []), ct));
        Assert.Empty(await store.List("one", ct));
    }
}
