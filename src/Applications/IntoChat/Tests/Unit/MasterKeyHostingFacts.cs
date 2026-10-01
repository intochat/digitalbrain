using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Aspire.Hosting;

namespace IntoChat.Tests.Unit;

public sealed class MasterKeyHostingFacts
{
    [Fact]
    public async Task Referencing_a_brain_supplies_the_same_master_key_to_each_runtime_but_not_clients()
    {
        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions { Args = [], DisableDashboard = true });
        var brain = builder.AddDigitalBrain("modules", persistentStorage: false);
        var first = builder.AddExecutable("first", "unused", ".").WithReference(brain);
        var second = builder.AddExecutable("second", "unused", ".").WithReference(brain);
        var client = builder.AddExecutable("client", "unused", ".").WithReference(brain.AsClient());

        var parameter = Assert.Single(builder.Resources.OfType<ParameterResource>());
        Assert.Equal("digitalbrain-master-key", parameter.Name);
        Assert.True(parameter.Secret);
        Assert.NotNull(parameter.Default);
        Assert.Same(parameter, (await EnvironmentOf(builder, first.Resource))["DigitalBrain__MasterKey"]);
        Assert.Same(parameter, (await EnvironmentOf(builder, second.Resource))["DigitalBrain__MasterKey"]);
        Assert.DoesNotContain("DigitalBrain__MasterKey", (await EnvironmentOf(builder, client.Resource)).Keys);
    }

    [Fact]
    public void Publishing_a_brain_requires_a_master_key_secret_without_a_generated_default()
    {
        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions
        {
            Args = ["--publisher", "manifest"],
            DisableDashboard = true,
        });
        builder.AddDigitalBrain("modules", persistentStorage: false);

        var parameter = Assert.Single(builder.Resources.OfType<ParameterResource>());
        Assert.True(parameter.Secret);
        Assert.Null(parameter.Default);
    }

    private static async Task<Dictionary<string, object>> EnvironmentOf(IDistributedApplicationBuilder builder, IResource resource)
    {
        var environment = new Dictionary<string, object>();
        var context = new EnvironmentCallbackContext(builder.ExecutionContext, resource, environment, TestContext.Current.CancellationToken);
        foreach (var callback in resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
        {
            await callback.Callback(context);
        }
        return environment;
    }
}
