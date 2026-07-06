using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace DigitalBrain.Tests.Kernel;

// Regression coverage for the task-23 "/health returns 500 under aspire run" bug. HealthEndpointTests.cs
// exercises Program.cs via WebApplicationFactory<Program>, which never sets ConnectionStrings__clustering/
// grainstate/journal, so it takes the isAspireHosted=false branch and never calls
// AddKeyedAzureTableServiceClient / AddKeyedAzureBlobServiceClient / AddAzureBlobServiceClient at all --
// the exact registrations that were at fault. This test instead replicates Program.cs's isAspireHosted=true
// Azure client wiring directly against a bare IHostApplicationBuilder.
public class AzureClientHealthCheckRegistrationTests
{
    [Fact]
    public void AspireHostedBlobServiceClientRegistrations_HealthCheckFactoriesResolveWithoutNetwork()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["ConnectionStrings:clustering"] = "UseDevelopmentStorage=true";
        builder.Configuration["ConnectionStrings:grainstate"] = "UseDevelopmentStorage=true";

        // Mirrors DigitalBrain.Kernel/Program.cs's isAspireHosted branch.
        builder.AddKeyedAzureTableServiceClient("clustering");
        builder.AddKeyedAzureBlobServiceClient("grainstate");
        builder.AddAzureBlobServiceClient("grainstate", settings => settings.DisableHealthChecks = true);

        using var host = builder.Build();
        var options = host.Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value;
        var registrations = options.Registrations;

        Assert.Contains(registrations, registration => registration.Name == "Azure_TableServiceClient_clustering");
        Assert.Contains(registrations, registration => registration.Name == "Azure_BlobServiceClient_grainstate");
        Assert.DoesNotContain(registrations, registration => registration.Name == "Azure_BlobServiceClient");

        // Before the fix, the unkeyed blob health-check factory resolved GetRequiredService<BlobServiceClient>()
        // and threw before DefaultHealthCheckService could convert the problem into an Unhealthy entry. Constructing
        // factories catches that DI bug without executing CheckHealthAsync(), which would perform real Azure SDK
        // calls against missing local Azurite (127.0.0.1:10000) and spam retry stack traces into successful runs.
        foreach (var registration in registrations)
        {
            Assert.NotNull(registration.Factory(host.Services));
        }
    }
}
