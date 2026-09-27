using DigitalBrain.Core;
using IntoChat;
using Microsoft.Extensions.Options;
using Xunit;

namespace IntoChat.Tests;

public sealed class CSharpCatalogFacts
{
    [Theory]
    [InlineData("../other")]
    [InlineData("other/program")]
    [InlineData("C:\\private")]
    public void ModelCannotSelectAnExternalScopeOrPath(string id)
        => Assert.Throws<ArgumentException>(() => CSharpCatalogStore.FileKey("workspace-trusted", id));

    [Fact]
    public void WorkspacesReceiveDifferentNeuronKeys()
        => Assert.NotEqual(CSharpCatalogStore.FileKey("workspace-a", "invoice"), CSharpCatalogStore.FileKey("workspace-b", "invoice"));

    [Fact]
    public async Task CatalogPersistsSeparatesWorkspacesAndKeepsTheNameWhenOnlySourceChanges()
    {
        var ct = TestContext.Current.CancellationToken;
        var indexes = new InMemoryDocumentStore<CSharpCatalogIndex>();
        var items = new InMemoryDocumentStore<CSharpCatalogItem>();
        var store = new CSharpCatalogStore(indexes, items);

        await store.Describe("one", "timer", "Timer status", "Show ticks", ct);
        await store.Describe("one", "timer", null, null, ct);

        var restarted = new CSharpCatalogStore(indexes, items);
        var described = Assert.Single(await restarted.List("one", ct));
        Assert.Equal(("Timer status", "Show ticks"), (described.Name, described.Purpose));
        Assert.Empty(await restarted.List("two", ct));
        await restarted.Remove("one", "timer", ct);
        Assert.Empty(await restarted.List("one", ct));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => restarted.Read("one", "timer", ct));
    }

    [Fact]
    public async Task CatalogRejectsInvalidIdsAndOversizedDescriptions()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new CSharpCatalogStore(new InMemoryDocumentStore<CSharpCatalogIndex>(), new InMemoryDocumentStore<CSharpCatalogItem>());

        await Assert.ThrowsAsync<ArgumentException>(() => store.Describe("one", "../timer", "Name", "", ct));
        await Assert.ThrowsAsync<ArgumentException>(() => store.Describe("one", "timer", "Name", new string('x', 4001), ct));
        Assert.Empty(await store.List("one", ct));
    }

    [Fact]
    public async Task AHostWithoutTheCSharpModuleRefusesToRunFiles()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new CSharpCatalogStore(new InMemoryDocumentStore<CSharpCatalogIndex>(), new InMemoryDocumentStore<CSharpCatalogItem>());
        var service = new CSharpToolService(null!, store, Options.Create(new CSharpAuthoringOptions { AllowActivation = true }));

        Assert.False(service.CanRun);
        Assert.False(service.AllowActivation);
        var tools = service.ForScope("scope");
        await Assert.ThrowsAsync<InvalidOperationException>(() => tools.List(ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => tools.Write("timer", "Console.WriteLine(1);", null, null, ct));
        Assert.Throws<InvalidOperationException>(() => tools.Contracts([]));
    }
}
