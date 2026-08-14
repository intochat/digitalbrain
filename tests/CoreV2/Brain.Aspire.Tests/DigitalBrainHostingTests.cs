using DigitalBrain.Aspire;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Orleans;
using Orleans.Configuration;
using Orleans.Runtime;
using Xunit;

namespace Brain.Aspire.Tests;

public sealed class DigitalBrainHostingTests
{
    [Fact]
    public void Runtime_extension_configures_a_silo_and_default_storage()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:journal"] = "UseDevelopmentStorage=true",
        });

        builder.AddDigitalBrainRuntime();
        using var host = builder.Build();

        Assert.NotNull(host.Services.GetService<ILocalSiloDetails>());
        Assert.Equal(
            TimeSpan.FromMinutes(10),
            host.Services.GetRequiredService<IOptions<SiloMessagingOptions>>().Value.ResponseTimeout);
    }

    [Fact]
    public void Runtime_extension_refuses_to_start_without_durable_journal_storage()
    {
        var builder = Host.CreateApplicationBuilder();

        var failure = Assert.Throws<InvalidOperationException>(() => builder.AddDigitalBrainRuntime());

        Assert.Contains("journal", failure.Message, StringComparison.Ordinal);
        Assert.Contains("durability", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Client_extension_never_registers_a_silo()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.AddDigitalBrainClient();

        Assert.DoesNotContain(builder.Services, descriptor => descriptor.ServiceType == typeof(ILocalSiloDetails));
        Assert.Contains(builder.Services, descriptor => descriptor.ServiceType == typeof(IClusterClient));
        using var host = builder.Build();
        Assert.Equal(
            TimeSpan.FromMinutes(10),
            host.Services.GetRequiredService<IOptions<ClientMessagingOptions>>().Value.ResponseTimeout);
    }
}
