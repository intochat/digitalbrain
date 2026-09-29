using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Core;
using DigitalBrain.Postgres;
using DigitalBrain.Postgres.Aspire.Hosting;

namespace DigitalBrain.Modules.Postgres.Tests.Unit;

public sealed class PostgresAspireHostingFacts
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ManagedDatabaseIsProjectedWithTheConfiguredConnectionNameAndReadiness(bool persistent)
    {
        var builder = CreateBuilder();
        var brain = builder.AddDigitalBrain("brain", persistentStorage: false);
        brain.AddModules([Definition(enabled: true, persistent)]);
        var consumer = builder.AddExecutable("consumer", "unused", ".").WithReference(brain);

        var server = Assert.Single(builder.Resources.OfType<PostgresServerResource>());
        var database = Assert.Single(builder.Resources.OfType<PostgresDatabaseResource>());
        Assert.Same(server, database.Parent);
        Assert.Equal("analytics", database.DatabaseName);
        Assert.Contains(consumer.Resource.Annotations.OfType<WaitAnnotation>(), wait =>
            ReferenceEquals(wait.Resource, database) && wait.WaitType == WaitType.WaitUntilHealthy);
        Assert.Equal(persistent, server.Annotations.OfType<ContainerMountAnnotation>().Any(mount => mount.Type == ContainerMountType.Volume));
        Assert.Equal(persistent, server.Annotations.OfType<ContainerLifetimeAnnotation>().Any(lifetime => lifetime.Lifetime == ContainerLifetime.Persistent));
        var environment = await EnvironmentOf(builder, consumer.Resource);
        Assert.Contains("ConnectionStrings__reporting", environment.Keys);
        Assert.Equal("reporting", environment["DigitalBrain__Postgres__ConnectionName"]);
    }

    [Fact]
    public async Task ExternalConnectionIsProjectedAsASecretWithoutProvisioningAServer()
    {
        var builder = CreateBuilder();
        builder.Configuration["ConnectionStrings:reporting"] = "Host=external;Database=analytics;Username=reader;Password=test-only";
        var brain = builder.AddDigitalBrain("brain", persistentStorage: false)
            .WithModule<PostgresModule>(module => module.WithPostgres().WithConnection("reporting"));
        var consumer = builder.AddExecutable("consumer", "unused", ".").WithReference(brain);

        Assert.Empty(builder.Resources.OfType<PostgresServerResource>());
        var environment = await EnvironmentOf(builder, consumer.Resource);
        var connection = Assert.IsType<ParameterResource>(environment["ConnectionStrings__reporting"]);
        Assert.True(connection.Secret);
        Assert.Equal(builder.Configuration["ConnectionStrings:reporting"], await connection.GetValueAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ModuleConfigurationCreatesOneDatabaseForMultipleConsumers()
    {
        var builder = CreateBuilder();
        var brain = builder.AddDigitalBrain("brain", persistentStorage: false)
            .WithModule<PostgresModule>(module => module.WithConnection("reporting").WithPostgres(options =>
            {
                options.DatabaseName = "analytics";
                options.PersistentStorage = false;
            }));
        var first = builder.AddExecutable("first", "unused", ".").WithReference(brain);
        var second = builder.AddExecutable("second", "unused", ".").WithReference(brain);
        var database = Assert.Single(builder.Resources.OfType<PostgresDatabaseResource>());
        Assert.Equal("analytics", database.DatabaseName);
        Assert.Single(builder.Resources.OfType<PostgresServerResource>());
        foreach (var consumer in new[] { first, second })
        {
            Assert.Contains(consumer.Resource.Annotations.OfType<WaitAnnotation>(), wait => ReferenceEquals(wait.Resource, database));
            Assert.Contains("ConnectionStrings__reporting", (await EnvironmentOf(builder, consumer.Resource)).Keys);
        }
    }

    [Fact]
    public void RepeatedHostingConfigurationDoesNotCreateDuplicateResources()
    {
        var builder = CreateBuilder();
        var brain = builder.AddDigitalBrain("brain", persistentStorage: false);
        brain.AddModules([Definition(enabled: true, persistent: false)]);
        var hosting = new PostgresModuleHosting();
        hosting.Configure(brain);
        builder.AddExecutable("consumer", "unused", ".").WithReference(brain);
        Assert.Single(builder.Resources.OfType<PostgresServerResource>());
        Assert.Single(builder.Resources.OfType<PostgresDatabaseResource>());
        brain.GetModuleConfiguration<PostgresModule>()["DigitalBrain:Postgres:Hosting:DatabaseName"] = "different";
        Assert.Throws<InvalidOperationException>(() => hosting.Configure(brain));
        Assert.Single(builder.Resources.OfType<PostgresDatabaseResource>());
    }

    private static ModuleDefinition Definition(bool enabled, bool persistent)
        => new(typeof(PostgresModule), new Dictionary<string, string?>
        {
            ["DigitalBrain:Postgres:ConnectionName"] = "reporting",
            ["DigitalBrain:Postgres:Hosting:Enabled"] = enabled.ToString(),
            ["DigitalBrain:Postgres:Hosting:DatabaseName"] = "analytics",
            ["DigitalBrain:Postgres:Hosting:PersistentStorage"] = persistent.ToString(),
        });

    private static IDistributedApplicationBuilder CreateBuilder()
        => DistributedApplication.CreateBuilder(new DistributedApplicationOptions { Args = [], DisableDashboard = true });

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
