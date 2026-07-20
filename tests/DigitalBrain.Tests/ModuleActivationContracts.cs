using DigitalBrain.AI;
using DigitalBrain.Kernel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class ModuleActivationContracts
{
    [Fact(DisplayName = "a referenced module remains inactive until AppHost selects it")]
    public void AvailableModuleIsNotAutomaticallySelected()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.UseOrleans(silo => silo.AddDigitalBrain());

        Assert.DoesNotContain(builder.Services, IsChatClient);
    }

    [Fact(DisplayName = "AppHost selection activates the generated silo module")]
    public void SelectedModuleIsConfigured()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["DigitalBrain:Modules:0"] = typeof(AIModule).FullName;

        builder.UseOrleans(silo => silo.AddDigitalBrain());

        Assert.Contains(builder.Services, IsChatClient);
    }

    [Fact(DisplayName = "the silo rejects an AppHost module absent from its generated catalog")]
    public void MissingModuleFailsComposition()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["DigitalBrain:Modules:0"] = "DigitalBrain.Google.GoogleModule";

        var failure = Assert.Throws<InvalidOperationException>(() =>
        {
            builder.UseOrleans(silo => silo.AddDigitalBrain());
        });

        Assert.Contains("DigitalBrain.Google.GoogleModule", failure.Message, StringComparison.Ordinal);
    }

    private static bool IsChatClient(ServiceDescriptor descriptor) =>
        descriptor.ServiceType == typeof(IChatClient);
}
