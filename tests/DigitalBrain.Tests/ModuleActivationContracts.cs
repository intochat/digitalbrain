using DigitalBrain.AI;
using DigitalBrain.Kernel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans.Serialization;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class ModuleActivationContracts
{
    [Fact(DisplayName = "an available module prepares serializers without activating runtime services")]
    public void AvailableModulePreparesSerializationWithoutRuntimeActivation()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.UseOrleans(silo => silo.AddDigitalBrain());

        Assert.DoesNotContain(builder.Services, IsChatClient);

        using var host = builder.Build();
        var message = new ChatMessage(ChatRole.User, "wire contract");
        var serializer = host.Services.GetRequiredService<Serializer<ChatMessage>>();

        Assert.Equal(
            message.Text,
            serializer.Deserialize(serializer.SerializeToArray(message)).Text);
    }

    [Fact(DisplayName = "AppHost selection activates the generated silo module")]
    public void SelectedModuleIsConfigured()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["DigitalBrain:Modules:0"] = AIModule.Id.Value;

        builder.UseOrleans(silo => silo.AddDigitalBrain());

        Assert.Contains(builder.Services, IsChatClient);

        using var host = builder.Build();
        var message = new ChatMessage(ChatRole.User, "wire contract");
        var serializer = host.Services.GetRequiredService<Serializer<ChatMessage>>();

        Assert.Equal(
            message.Text,
            serializer.Deserialize(serializer.SerializeToArray(message)).Text);
    }

    [Fact(DisplayName = "the silo rejects an AppHost module absent from its generated catalog")]
    public void MissingModuleFailsComposition()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["DigitalBrain:Modules:0"] = "DigitalBrain.Missing.MissingModule";

        var failure = Assert.Throws<InvalidOperationException>(() =>
        {
            builder.UseOrleans(silo => silo.AddDigitalBrain());
        });

        Assert.Contains("DigitalBrain.Missing.MissingModule", failure.Message, StringComparison.Ordinal);
    }

    private static bool IsChatClient(ServiceDescriptor descriptor) =>
        descriptor.ServiceType == typeof(IChatClient);
}
