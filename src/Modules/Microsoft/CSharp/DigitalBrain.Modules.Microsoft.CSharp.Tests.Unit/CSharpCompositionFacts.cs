using DigitalBrain.AI.Agents;
using DigitalBrain.Microsoft.CSharp;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Modules.Microsoft.CSharp.Tests.Unit;

public sealed class CSharpCompositionFacts
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecutionKeepsPackageServicesWhileOnlyAuthoringRegistersConsoleTools(bool authoring)
    {
        var builder = UnitTest.Create().WithModule<CSharpModule>().WithReminders();
        if (authoring) { builder.WithModule<CSharpAuthoringModule>(); }
        await using var brain = await builder.StartAsync(TestContext.Current.CancellationToken);
        Assert.True(brain.SiloServices.GetRequiredService<CSharpToolService>().CanRun);
        Assert.NotNull(brain.SiloServices.GetRequiredService<CSharpCatalogStore>());
        var factories = brain.SiloServices.GetServices<IAgentToolFactory>().OfType<CSharpAgentTools>();
        if (authoring)
        {
            Assert.Single(factories);
            Assert.NotNull(brain.SiloServices.GetRequiredService<CSharpSharing>());
        }
        else
        {
            Assert.Empty(factories);
            Assert.Null(brain.SiloServices.GetService<CSharpSharing>());
        }
    }
}
