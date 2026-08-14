using DigitalBrain.Aspire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans;
using Orleans.Runtime;
using Xunit;

namespace Brain.Aspire.Tests;

public sealed class DigitalBrainHostingTests
{
    [Fact]
    public void Runtime_extension_configures_a_silo_and_default_storage()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.AddDigitalBrainRuntime();
        using var host = builder.Build();

        Assert.NotNull(host.Services.GetService<ILocalSiloDetails>());
    }

    [Fact]
    public void Client_extension_never_registers_a_silo()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.AddDigitalBrainClient();

        Assert.DoesNotContain(builder.Services, descriptor => descriptor.ServiceType == typeof(ILocalSiloDetails));
        Assert.Contains(builder.Services, descriptor => descriptor.ServiceType == typeof(IClusterClient));
    }
}
