using Microsoft.Extensions.Hosting;
using Aspire.Hosting;
using Aspire.Hosting.Azure;
using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Aspire.Server;
using DigitalBrain.Contracts;
using DigitalBrain.Contracts.Edge.V1;
using DigitalBrain.Kernel.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using System.Text.Json;

namespace DigitalBrain.Kernel.Tests.Unit;

public sealed class CompositionBoundaryFacts
{
    [Fact]
    public void TwoBrainsHaveDistinctResourcesAndModuleNodes()
    {
        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions { Args = [], DisableDashboard = true });
        var first = builder.AddDigitalBrain("first", persistentStorage: false, options: new() { UseAzureStorage = true }).WithModule("sample");
        var second = builder.AddDigitalBrain("second", persistentStorage: false, options: new() { UseAzureStorage = true }).WithModule("sample");
        Assert.NotSame(first.GetOrAddModuleNode("sample").Resource, second.GetOrAddModuleNode("sample").Resource);
        var names = builder.Resources.Select(r => r.Name).ToArray();
        Assert.Equal(names.Length, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains("first-sample", names);
        Assert.Contains("second-sample", names);
    }

    [Fact]
    public void UnknownSelectionIsRejectedBeforeAnyFactoryRuns()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration["DigitalBrain:Modules:a:Enabled"] = "true";
        builder.Configuration["DigitalBrain:Modules:z:Enabled"] = "true";
        var created = 0;
        Assert.Throws<InvalidOperationException>(() => builder.AddDigitalBrainServer(server =>
            server.AddModule("a", () => { created++; return new StatefulModule(); })));
        Assert.Equal(0, created);
    }

    [Fact]
    public void DuplicateRegistrationIsRejected()
    {
        var server = new DigitalBrainServerBuilder().AddModule<StatefulModule>("module");
        Assert.Throws<InvalidOperationException>(() => server.AddModule<StatefulModule>("module"));
    }

    [Fact]
    public async Task SiloAndHttpConfigurationUseTheSameInstanceAndUnselectedFactoriesDoNotRun()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration["DigitalBrain:Modules:module:Enabled"] = "true";
        var instance = new StatefulModule();
        var created = 0;
        builder.AddDigitalBrainServer(server => server
            .AddModule("module", () => { created++; return instance; })
            .AddModule("unused", () => throw new InvalidOperationException("Not selected")));
        await using var app = builder.Build();
        app.MapDigitalBrainModules();
        Assert.Equal(1, created);
        Assert.Equal(1, instance.SiloConfigurations);
        Assert.Equal(1, instance.HttpConfigurations);
        Assert.Same(instance, Assert.Single(app.Services.GetServices<IModule>()));
    }

    [Fact]
    public async Task ServerRefusesToStartWithoutAnAuthorizationPolicy()
    {
        var builder = WebApplication.CreateBuilder();
        builder.AddDigitalBrainServer(_ => { });
        builder.UseOrleans(silo => silo.UseLocalhostClustering().AddMemoryGrainStorageAsDefault().UseInMemoryReminderService());
        await using var app = builder.Build();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => app.StartAsync(TestContext.Current.CancellationToken));
        Assert.Contains("IBrainAccess", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DefaultAppHostDoesNotProvisionAzureAndRejectsDuplicatesBeforeAReferenceExists()
    {
        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions { Args = [], DisableDashboard = true });
        var brain = builder.AddDigitalBrain("brain").WithModule("module");
        Assert.Empty(builder.Resources.OfType<AzureStorageResource>());
        Assert.Throws<InvalidOperationException>(() => brain.WithModule("module"));
        Assert.Throws<InvalidOperationException>(() => brain.AddBlobContainer("requires-azure"));
    }

    [Fact]
    public void V1WirePathsAndJsonRemainStable()
    {
        Assert.Equal("scripts/v1/invoke", ScriptEdgeProtocol.Invoke);
        Assert.Equal("scripts/v1/signals", ScriptEdgeProtocol.Signals);
        Assert.Equal("X-DigitalBrain-Refusal", ScriptEdgeProtocol.RefusalHeader);
        var invocation = new ScriptInvocation("contract", "key", "method", [JsonSerializer.SerializeToElement(42)]);
        Assert.Equal("{\"contract\":\"contract\",\"key\":\"key\",\"method\":\"method\",\"arguments\":[42]}", JsonSerializer.Serialize(invocation, ScriptEdgeProtocol.Json));
    }

    [Fact]
    public async Task ConcurrentUsageIsCollectedAndSnapshotsAreDetached()
    {
        using var context = IntentContext.Begin("intent");
        await Task.WhenAll(Enumerable.Range(0, 100).Select(_ => Task.Run(() =>
        {
            Assert.Same(context, IntentContext.Current);
            context.AddUsage(new Entry());
        }, TestContext.Current.CancellationToken)));
        var snapshot = context.Snapshot();
        context.AddUsage(new Entry());
        Assert.Equal(100, snapshot.Usage.Length);
        Assert.Equal(101, context.Usage.Length);
    }

    [Fact]
    public void IntentScopesEnforceNestingAndRestoreTheirParent()
    {
        using var outer = IntentContext.Begin("outer");
        var inner = IntentContext.Begin("inner");
        Assert.Throws<InvalidOperationException>(outer.Dispose);
        inner.Dispose();
        Assert.Same(outer, IntentContext.Current);
        inner.Dispose();
    }

    private sealed record Entry : IIntentUsageEntry;
    public sealed class StatefulModule : IModule, IHttpModule
    {
        public int SiloConfigurations { get; private set; }
        public int HttpConfigurations { get; private set; }
        public void Configure(ISiloBuilder silo) => SiloConfigurations++;
        public void Configure(IEndpointRouteBuilder endpoints) => HttpConfigurations++;
    }
}
