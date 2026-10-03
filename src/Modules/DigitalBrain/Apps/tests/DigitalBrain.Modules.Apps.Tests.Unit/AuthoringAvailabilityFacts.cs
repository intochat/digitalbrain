using DigitalBrain;
using DigitalBrain.Apps;
using DigitalBrain.Contracts;
using DigitalBrain.Kernel.Enforcement;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Modules.Apps.Tests.Unit;

public sealed class AuthoringAvailabilityFacts
{
    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    [InlineData(true)]
    public void AuthoringRoutesRequireARunnableSandbox(bool? canRun)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(typeof(IDigitalBrain), _ => null!);
        builder.Services.AddSingleton<MarketplaceService>(_ => null!);
        if (canRun is bool available) { builder.Services.AddSingleton<IScriptSandbox>(new Sandbox(available)); }
        IEndpointRouteBuilder app = builder.Build();
        new AppsModule().Configure(app);
        var routes = app.DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText).ToArray();
        foreach (var operation in new[] { "revisions", "fork", "pull", "proposals", "publish" })
        { Assert.Equal(canRun == true, routes.Contains("/packages/{owner}/{name}/" + operation)); }
        Assert.Contains("/packages/{owner}/{name}/spec", routes);
    }

    [Fact]
    public async Task MarketplacePackagesDoNotBecomeTrackedInstallsInAnotherBrain()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<AppsModule>().StartAsync(ct);
        Caller.As("alice");
        await brain.AuthorizeCallerAsync();
        var id = PackageId.Create("alice", "public");
        var package = brain.Get<IPackage>(id.ToString());
        var revision = await package.Commit(new(Guid.NewGuid(), null,
            new(new("Public", "Marketplace only", [new("ask", "Ask")], [], Runtime: "prompt"), ""), "Initial"));
        await package.Publish(new(Guid.NewGuid(), revision.Id));
        Assert.Empty(await brain.Get<IApps>(BrainScope.CurrentId()).List());
        Assert.Empty(await brain.Get<IApps>(BrainScope.CurrentId()).List());
    }

    private sealed class Sandbox(bool canRun) : IScriptSandbox
    {
        public bool CanRun => canRun;
        public string AuthoringDescription => "Test sandbox";
        public Task<ScriptContractCatalog> ReadContracts(IReadOnlyList<string> modules, CancellationToken cancellationToken)
            => Task.FromResult(new ScriptContractCatalog([], [], ""));
        public ScriptCompilationCheck Check(IReadOnlyDictionary<string, string> files) => new(true, []);
    }

    [Theory]
    [InlineData("prompt", false, false)]
    [InlineData("prompt", true, true)]
    [InlineData("csharp", false, true)]
    public async Task OnlyRevisionsThatRunScriptsNeedASandbox(string runtime, bool hasTests, bool requiresSandbox)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<AppsModule>().StartAsync(ct);
        Caller.As("alice");
        await brain.AuthorizeCallerAsync();
        var id = PackageId.Create("alice", "availability");
        var files = new Dictionary<string, string>();
        if (hasTests) { files[PackageContent.TestsPath] = "Console.WriteLine(1);"; }
        var content = new PackageContent(new("Availability", "Checks sandbox policy", [new("ask", "Answer")], [], Runtime: runtime),
            runtime == "csharp" ? "Console.WriteLine(1);" : "", files);
        var revision = await brain.Get<IPackage>(id.ToString()).Commit(new(Guid.NewGuid(), null, content, "Check availability"));
        var service = brain.SiloServices.GetRequiredService<MarketplaceService>();
        if (requiresSandbox)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.RequireRunnable(new(id, revision.Id)));
        }
        else { await service.RequireRunnable(new(id, revision.Id)); }
        Assert.Equal(runtime, (await service.Spec(id, revision.Id)).Runtime);
    }
}
