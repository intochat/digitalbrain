using DigitalBrain.Apps;
using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Modules.Apps.Tests.Unit.Applications;

public sealed class ApplicationFacts
{
    [Fact]
    public void AppRequirementsReuseDeclaredModulesAndRespectFrozenComposition()
    {
        var composition = new BrainCompositionBuilder().WithModule<AppsModule>();
        var app = AppDefinition.Of<SampleApp>();

        composition.WithApp(app).WithApp(app);
        var built = composition.Build();

        Assert.Single(app.RequiredModules);
        Assert.Single(built.Modules, module => module.ModuleType == typeof(AppsModule));
        Assert.Throws<InvalidOperationException>(() => composition.WithApp(app));
    }

    [Fact]
    public async Task CatalogStartsOnlyTheSelectedApplicationInOrderForItsKey()
    {
        var calls = new List<string>();
        var definition = new AppDefinition("example", [], [
            start => { calls.Add("surface:" + start.Key); return Task.CompletedTask; },
            start => { calls.Add("neuron:" + start.Key); return Task.CompletedTask; },
        ]);
        await using var brain = await UnitTest.Create()
            .ConfigureSilo(silo => silo.AddApplication(definition))
            .StartAsync(TestContext.Current.CancellationToken);
        var catalog = brain.SiloServices.GetRequiredService<ApplicationCatalog>();

        await catalog.Start("example", "workspace/app");

        Assert.Equal(["surface:workspace/app", "neuron:workspace/app"], calls);
        await Assert.ThrowsAsync<ArgumentException>(() => catalog.Start("missing", "workspace/app"));
        await Assert.ThrowsAsync<ArgumentException>(() => catalog.Start("example", ""));
        Assert.Equal(2, calls.Count);
    }

    private sealed class SampleApp : IApplication
    {
        public void Configure(IAppBuilder app) => app.RequireModule<AppsModule>().RequireModule<AppsModule>();
    }
}
