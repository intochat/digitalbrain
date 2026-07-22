using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Google;
using DigitalBrain.Google.Aspire.Hosting;
using DigitalBrain.Salesforce;
using DigitalBrain.Salesforce.Aspire.Hosting;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class IntegrationHostingContracts
{
    [Fact(DisplayName = "AppHost prompts once for official Gmail and Salesforce OAuth parameters")]
    public void AppHostPromptsOnceForOfficialOAuthParameters()
    {
        var builder = DistributedApplication.CreateBuilder();
        var brain = builder.AddBrain("brain");

        brain.AddModule<GoogleModule>(google => google.WithGmail());
        brain.AddModule<SalesforceModule>(salesforce => salesforce.WithSalesforce());

        AssertParameter(builder, "google-client-id", secret: false);
        AssertParameter(builder, "google-client-secret", secret: true);
        AssertParameter(builder, "google-redirect-uri", secret: false);
        AssertParameter(builder, "salesforce-client-id", secret: false);
        AssertParameter(builder, "salesforce-client-secret", secret: true);
        AssertParameter(builder, "salesforce-redirect-uri", secret: false);
    }

    [Fact(DisplayName = "integration OAuth configuration is projected only to the silo")]
    public async Task IntegrationOAuthConfigurationIsProjectedOnlyToTheSilo()
    {
        var builder = DistributedApplication.CreateBuilder();
        var brain = builder
            .AddBrain("brain")
            .WithDevelopmentStores();

        brain.AddModule<GoogleModule>(google => google.WithGmail());
        brain.AddModule<SalesforceModule>(salesforce => salesforce.WithSalesforce());

        var silo = builder.AddResource(new ProjectionProbe("silo")).WithReference(brain);
        var client = builder.AddResource(new ProjectionProbe("client")).WithReference(brain.AsClient());
        var siloEnvironment = await ProjectAsync(silo.Resource);
        var clientEnvironment = await ProjectAsync(client.Resource);

        Assert.Same(
            Parameter(builder, "google-client-secret"),
            siloEnvironment["DigitalBrain__Google__Gmail__ClientSecret"]);
        Assert.Same(
            Parameter(builder, "salesforce-client-secret"),
            siloEnvironment["DigitalBrain__Salesforce__ClientSecret"]);
        Assert.Contains("DigitalBrain__Google__Gmail__ClientId", siloEnvironment.Keys);
        Assert.Contains("DigitalBrain__Google__Gmail__RedirectUri", siloEnvironment.Keys);
        Assert.Contains("DigitalBrain__Salesforce__ClientId", siloEnvironment.Keys);
        Assert.Contains("DigitalBrain__Salesforce__RedirectUri", siloEnvironment.Keys);
        Assert.DoesNotContain(
            clientEnvironment.Keys,
            key => key.StartsWith("DigitalBrain__", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "AppHost rejects duplicate Gmail and Salesforce selections")]
    public void AppHostRejectsDuplicateIntegrationSelections()
    {
        var googleBuilder = DistributedApplication.CreateBuilder();
        var googleBrain = googleBuilder.AddBrain("google-brain");
        var salesforceBuilder = DistributedApplication.CreateBuilder();
        var salesforceBrain = salesforceBuilder.AddBrain("salesforce-brain");

        Assert.Throws<InvalidOperationException>(
            () => googleBrain.AddModule<GoogleModule>(google => google
                .WithGmail()
                .WithGmail()));
        Assert.Throws<InvalidOperationException>(
            () => salesforceBrain.AddModule<SalesforceModule>(salesforce => salesforce
                .WithSalesforce()
                .WithSalesforce()));
    }

    private static void AssertParameter(
        IDistributedApplicationBuilder builder,
        string name,
        bool secret)
    {
        var parameter = Parameter(builder, name);

        Assert.Equal(secret, parameter.Secret);
        Assert.False(string.IsNullOrWhiteSpace(parameter.Description));
    }

    private static ParameterResource Parameter(IDistributedApplicationBuilder builder, string name)
        => Assert.IsType<ParameterResource>(
            Assert.Single(builder.Resources, resource => resource.Name == name));

    private static async Task<Dictionary<string, object>> ProjectAsync(IResourceWithEnvironment resource)
    {
        var context = new EnvironmentCallbackContext(
            new DistributedApplicationExecutionContext(DistributedApplicationOperation.Publish));

        foreach (var annotation in resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
        {
            await annotation.Callback(context);
        }

        return context.EnvironmentVariables.ToDictionary(
            entry => entry.Key,
            entry => entry.Value,
            StringComparer.Ordinal);
    }

    private sealed class ProjectionProbe(string name) : Resource(name), IResourceWithEnvironment, IResourceWithEndpoints;
}
