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

        // Resolve the model's connection strings without starting storage or silo processes.
        foreach (var endpoint in builder.Resources.SelectMany(resource => resource.Annotations.OfType<EndpointAnnotation>()))
        {
            var port = endpoint.Port ?? endpoint.TargetPort ?? 17000;
            endpoint.AllocatedEndpoint = new AllocatedEndpoint(endpoint, "localhost", port,
                port.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        var parameter = builder.Resources.OfType<ParameterResource>().Single(resource => resource.Name == DigitalBrainHostingNames.MasterKeyParameter);
        var expected = await parameter.GetValueAsync(TestContext.Current.CancellationToken);
        var firstEnvironment = await EnvironmentOf(builder, first.Resource);
        var secondEnvironment = await EnvironmentOf(builder, second.Resource);
        Assert.Equal(expected, firstEnvironment[DigitalBrainNames.MasterKeyEnvironmentVariable]);
        Assert.Equal(firstEnvironment[DigitalBrainNames.MasterKeyEnvironmentVariable], secondEnvironment[DigitalBrainNames.MasterKeyEnvironmentVariable]);
        Assert.DoesNotContain(DigitalBrainNames.MasterKeyEnvironmentVariable, (await EnvironmentOf(builder, client.Resource)).Keys);
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

    private static async Task<IReadOnlyDictionary<string, string>> EnvironmentOf(IDistributedApplicationBuilder builder, IResource resource)
    {
        var configuration = await ExecutionConfigurationBuilder.Create(resource)
            .WithEnvironmentVariablesConfig()
            .BuildAsync(builder.ExecutionContext, NullLogger.Instance, TestContext.Current.CancellationToken);
        return configuration.EnvironmentVariables.ToDictionary();
    }
}
