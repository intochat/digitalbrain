using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace IntoChat.Tests.Unit;

public sealed class MasterKeyHostingFacts
{
    [Fact]
    public async Task Referencing_a_brain_supplies_the_same_master_key_to_each_runtime_but_not_clients()
    {
        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions { Args = [], DisableDashboard = true });
        builder.Configuration[$"Parameters:{DigitalBrainHostingNames.MasterKeyParameter}"] = "hosting test master key";
        var brain = builder.AddDigitalBrain("modules", persistentStorage: false);
        var first = builder.AddExecutable("first", "unused", ".").WithReference(brain);
        var second = builder.AddExecutable("second", "unused", ".").WithReference(brain);
        var client = builder.AddExecutable("client", "unused", ".").WithReference(brain.AsClient());

        var parameter = builder.Resources.OfType<ParameterResource>().Single(resource => resource.Name == DigitalBrainHostingNames.MasterKeyParameter);
        Assert.True(parameter.Secret);
        Assert.NotNull(parameter.Default);
        var firstEnvironment = await EnvironmentOf(first.Resource);
        var secondEnvironment = await EnvironmentOf(second.Resource);
        Assert.Same(parameter, firstEnvironment[DigitalBrainNames.MasterKeyEnvironmentVariable]);
        Assert.Same(parameter, secondEnvironment[DigitalBrainNames.MasterKeyEnvironmentVariable]);
        Assert.DoesNotContain(DigitalBrainNames.MasterKeyEnvironmentVariable, (await EnvironmentOf(client.Resource)).Keys);
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

        var parameter = builder.Resources.OfType<ParameterResource>().Single(resource => resource.Name == DigitalBrainHostingNames.MasterKeyParameter);
        Assert.True(parameter.Secret);
        Assert.Null(parameter.Default);
    }

    private static async Task<IReadOnlyDictionary<string, object>> EnvironmentOf(IResource resource)
    {
        // Publish expression resolution inspects the run model without waiting for endpoint allocation.
        var configuration = await ExecutionConfigurationBuilder.Create(resource)
            .WithEnvironmentVariablesConfig()
            .BuildAsync(new DistributedApplicationExecutionContext(DistributedApplicationOperation.Publish),
                NullLogger.Instance, TestContext.Current.CancellationToken);
        return configuration.EnvironmentVariablesWithUnprocessed.ToDictionary(pair => pair.Key, pair => pair.Value.Unprocessed);
    }
}
