using DigitalBrain.Apps;
using DigitalBrain.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Modules.Apps.Tests.Unit;

public sealed class ShippedAppPublisherFacts
{
    [Fact]
    public async Task SourcesPublishUnderTheirOwnIdentityAndUnchangedContentDoesNotCreateAnotherRevision()
    {
        var ct = TestContext.Current.CancellationToken;
        var runner = new ScriptedTestRunner();
        runner.BySourceMarker["passing"] = (0, "dbtest:pass Echo");
        runner.BySourceMarker["failing"] = (1, "dbtest:fail Echo\tWrong answer");
        await using var brain = await UnitTest.Create().WithModule<AppsModule>()
            .ConfigureSilo(silo => silo.Services.AddSingleton<ITestScriptRunner>(runner)).StartAsync(ct);
        var sources = new IShippedAppSource[] { new Source("first"), new Source("second"), new Source("red") };
        var service = new MarketplaceService(brain.SiloServices.GetRequiredService<IDigitalBrain>(), new AppAuthoringPolicy([], new Sandbox()));
        async Task Ship(string selection)
        {
            using var publisher = new ShippedAppPublisher(brain.SiloServices.GetRequiredService<IDigitalBrain>(), service,
                sources, new StartedLifetime(), new ConfigurationBuilder().AddInMemoryCollection(
                    new Dictionary<string, string?> { [ShippedAppPublisher.ShipOnStartupKey] = selection }).Build(),
                NullLogger<ShippedAppPublisher>.Instance);
            await publisher.StartAsync(ct);
            await publisher.ExecuteTask!.WaitAsync(ct);
        }
        await Ship("false");
        var first = brain.Get<IPackage>("first/echo");
        Assert.Null((await first.Read()).Head);
        await Ship("true");
        var initial = await first.Read();
        Assert.NotNull(initial.Published);
        Assert.NotNull((await brain.Get<IPackage>("second/echo").Read()).Published);
        Assert.Null((await brain.Get<IPackage>("red/echo").Read()).Published);
        await Ship("true");
        Assert.Equal(initial.Head, (await first.Read()).Head);
        Assert.Equal(initial.Published, (await first.Read()).Published);
    }

    private sealed class Source(string publisher) : IShippedAppSource
    {
        public string Publisher => publisher;
        public IReadOnlyList<ShippedApp> Load() =>
        [new(PackageId.Create(publisher, "echo"), new(new("Echo", "Echo input", [new("ask", "Echo")], [], Runtime: "prompt"), "",
            new Dictionary<string, string> { [PackageContent.TestsPath] = publisher == "red" ? "failing" : "passing" }))];
    }

    private sealed class Sandbox : IScriptSandbox
    {
        public bool CanRun => true;
        public string AuthoringDescription => "Script sandbox";
        public Task<ScriptContractCatalog> ReadContracts(IReadOnlyList<string> modules, CancellationToken cancellationToken)
            => Task.FromResult(new ScriptContractCatalog([], [], ""));
        public ScriptCompilationCheck Check(IReadOnlyDictionary<string, string> files) => new(true, []);
    }

    private sealed class StartedLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => new(true);
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication() { }
    }
}
