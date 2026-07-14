using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace DigitalBrain.Tests.Kernel;

public class AzureClientHealthCheckRegistrationTests
{
    [Fact]
    public void AspireHostedBlobServiceClientRegistrations_HealthCheckFactoriesResolveWithoutNetwork()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["ConnectionStrings:clustering"] = "UseDevelopmentStorage=true";
        builder.Configuration["ConnectionStrings:grainstate"] = "UseDevelopmentStorage=true";

        builder.AddKeyedAzureTableServiceClient("clustering");
        builder.AddKeyedAzureBlobServiceClient("grainstate");
        builder.AddAzureBlobServiceClient("grainstate", settings => settings.DisableHealthChecks = true);

        using var host = builder.Build();
        var options = host.Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value;
        var registrations = options.Registrations;

        Assert.Contains(registrations, registration => registration.Name == "Azure_TableServiceClient_clustering");
        Assert.Contains(registrations, registration => registration.Name == "Azure_BlobServiceClient_grainstate");
        Assert.DoesNotContain(registrations, registration => registration.Name == "Azure_BlobServiceClient");

        foreach (var registration in registrations)
        {
            Assert.NotNull(registration.Factory(host.Services));
        }
    }
}
