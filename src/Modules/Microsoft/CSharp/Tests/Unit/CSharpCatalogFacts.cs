using System.Reflection;
using DigitalBrain.Contracts;
using DigitalBrain.Core;
using DigitalBrain.Microsoft.CSharp;

using Microsoft.Extensions.Options;
using Xunit;

namespace DigitalBrain.Microsoft.CSharp.Tests;

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
        var workspaces = new InMemoryDocumentStore<CSharpCatalog>();
        var store = new CSharpCatalogStore(workspaces);

        await store.Describe("one", "timer", "Timer status", "Show ticks", ct);
        await store.Describe("one", "timer", null, null, ct);

        var restarted = new CSharpCatalogStore(workspaces);
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
        var store = new CSharpCatalogStore(new InMemoryDocumentStore<CSharpCatalog>());

        await Assert.ThrowsAsync<ArgumentException>(() => store.Describe("one", "../timer", "Name", "", ct));
        await Assert.ThrowsAsync<ArgumentException>(() => store.Describe("one", "timer", "Name", new string('x', 4001), ct));
        Assert.Empty(await store.List("one", ct));
    }

    [Fact]
    public async Task AHostWithoutTheCSharpModuleRefusesToRunFiles()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new CSharpCatalogStore(new InMemoryDocumentStore<CSharpCatalog>());
        var service = new CSharpToolService(null!, store, Options.Create(new CSharpAuthoringOptions { AllowActivation = true }));

        Assert.False(service.CanRun);
        Assert.False(service.AllowActivation);
        var tools = service.ForScope("scope");
        await Assert.ThrowsAsync<InvalidOperationException>(() => tools.Write("timer", "Console.WriteLine(1);", null, null, ct));
        Assert.Throws<InvalidOperationException>(() => tools.Contracts([]));
    }

    [Fact]
    public async Task ARejectedDescriptionStoresNoSource()
    {
        var ct = TestContext.Current.CancellationToken;
        var brain = new RecordingBrain();
        var service = new CSharpToolService(brain, new CSharpCatalogStore(new InMemoryDocumentStore<CSharpCatalog>()),
            Options.Create(new CSharpAuthoringOptions()), new CSharpContractCatalog(Options.Create(new CSharpOptions())));

        await Assert.ThrowsAsync<ArgumentException>(() => service.ForScope("scope").Write("timer", "Console.WriteLine(1);", new string('n', 121), null, ct));

        Assert.Empty(brain.Calls);
    }

    private sealed class RecordingBrain : IDigitalBrain
    {
        public List<string> Calls { get; } = [];

        public T Get<T>(string id) where T : class, IGrainWithStringKey
        {
            var proxy = DispatchProxy.Create<T, CallRecorder>();
            ((CallRecorder)(object)proxy).Calls = Calls;
            return proxy;
        }

        public Task<ISignalSubscription<T>> SubscribeAsync<T>(INeuron source, CancellationToken cancellationToken = default) where T : Signal
            => throw new NotSupportedException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    public class CallRecorder : DispatchProxy
    {
        internal List<string> Calls { get; set; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Calls.Add(targetMethod!.Name);
            return Task.CompletedTask;
        }
    }
}
