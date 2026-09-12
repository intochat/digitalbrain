using System.Globalization;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Aspire.Hosting;

namespace DigitalBrain.ClickHouse.Aspire.Hosting;

public static class ClickHouseHostingExtensions
{
    public static DigitalBrainModuleBuilder<ClickHouseModule> WithClickHouse(
        this DigitalBrainModuleBuilder<ClickHouseModule> module,
        Action<ClickHouseHostingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(module);
        var options = new ClickHouseHostingOptions();
        configure?.Invoke(options);
        State(module).Enable(options);
        return module;
    }

    private static ClickHouseHostingState State(DigitalBrainModuleBuilder<ClickHouseModule> module)
    {
        var state = module.Brain.GetOrAddState(static brain => new ClickHouseHostingState(brain), out var added);
        if (added)
        {
            module.AddProjection(state);
        }

        return state;
    }

    internal static string ReadSeed(string name)
    {
        var resourceName = $"Seeds/{name}.sql";
        using var stream = typeof(ClickHouseHostingExtensions).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"ClickHouse seed '{name}' is not an embedded resource under Seeds/.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private sealed class ClickHouseHostingState(DigitalBrainBuilder brain) : DigitalBrainModuleProjection
    {
        private IResourceBuilder<ClickHouseServerResource>? _server;
        private IResourceBuilder<ClickHouseDatabaseResource>? _database;
        private bool _enabled;

        internal void Enable(ClickHouseHostingOptions options)
        {
            if (_enabled)
            {
                return;
            }

            var builder = brain.ApplicationBuilder;
            _server = builder.AddClickHouse(ClickHouseNames.Server)
                .WithDataVolume()
                .WithLifetime(ContainerLifetime.Persistent)
                .WithParentRelationship(brain.Resource)
                .WithUrlForEndpoint("http", static endpoint => new ResourceUrlAnnotation
                {
                    Url = "/play",
                    DisplayText = "ClickHouse Play",
                    Endpoint = endpoint,
                });
            _database = _server.AddDatabase(ClickHouseNames.DatabaseResource, ClickHouseNames.DatabaseName);

            if (options.Seeds.Count > 0)
            {
                // Init scripts run alphabetically on an empty data dir; the numeric prefix keeps the order added.
                var seeds = options.Seeds.Select((name, index) => (ContainerFileSystemItem)new ContainerFile
                {
                    Name = $"{(index + 1).ToString("000", CultureInfo.InvariantCulture)}-{name}.sql",
                    Contents = ReadSeed(name),
                }).ToArray();
                _server.WithContainerFiles("/docker-entrypoint-initdb.d", seeds);
            }

            if (options.AlwaysRunInitScripts)
            {
                _server.WithEnvironment("CLICKHOUSE_ALWAYS_RUN_INITDB_SCRIPTS", "1");
            }

            _enabled = true;
        }

        public override void Apply<TResource>(IResourceBuilder<TResource> builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            if (!_enabled || brain.FakesEnabled || _database is null)
            {
                return;
            }

            // Waiting on the database resource covers server health and the CREATE DATABASE step.
            builder
                .WithReference(_database, connectionName: ClickHouseRegistration.DefaultConnectionName)
                .WithAnnotation(new WaitAnnotation(_database.Resource, WaitType.WaitUntilHealthy, exitCode: 0))
                .WithEnvironment(EnvironmentKeys.For(ClickHouseModule.ConfigurationRoot, "Provider"), ClickHouseModule.DriverProviderName)
                .WithEnvironment(EnvironmentKeys.For(ClickHouseModule.ConfigurationRoot, "ConnectionName"), ClickHouseRegistration.DefaultConnectionName);
        }
    }
}
