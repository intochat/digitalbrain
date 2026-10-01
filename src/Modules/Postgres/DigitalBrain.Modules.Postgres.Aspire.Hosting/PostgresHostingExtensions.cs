using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Aspire.Hosting;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Postgres.Aspire.Hosting;

internal static class PostgresHostingExtensions
{
    public static DigitalBrainModuleBuilder<PostgresModule> WithPostgres(
        this DigitalBrainModuleBuilder<PostgresModule> module,
        Action<PostgresHostingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(module);
        var options = new PostgresHostingOptions();
        configure?.Invoke(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ConnectionName);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.DatabaseName);
        State(module).Enable(new(options.ConnectionName, options.DatabaseName, options.PersistentStorage));
        return module;
    }

    internal static void WithExternalConnection(this DigitalBrainModuleBuilder<PostgresModule> module, string connectionName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionName);
        State(module).Enable(new(connectionName, null, false));
    }

    private static PostgresHostingState State(DigitalBrainModuleBuilder<PostgresModule> module)
    {
        var state = module.DigitalBrainBuilder.GetOrAddState(brain => new PostgresHostingState(brain, module.Resource), out var added);
        if (added) { module.AddProjection(state); }
        return state;
    }

    private sealed record HostingConfiguration(string ConnectionName, string? DatabaseName, bool PersistentStorage);

    private sealed class PostgresHostingState(DigitalBrainBuilder brain, IResourceBuilder<DigitalBrainModuleResource> module)
        : DigitalBrainModuleProjection
    {
        private HostingConfiguration? _configuration;
        private IResourceBuilder<PostgresDatabaseResource>? _database;
        private IResourceBuilder<ParameterResource>? _connection;

        internal void Enable(HostingConfiguration configuration)
        {
            if (_configuration is not null)
            {
                if (_configuration == configuration) { return; }
                throw new InvalidOperationException("Postgres hosting is already configured. Configure it once before referencing the brain.");
            }

            if (configuration.DatabaseName is { } databaseName)
            {
                var server = brain.ApplicationBuilder.AddPostgres("postgres-server")
                    .WithParentRelationship(module);
                if (!brain.ApplicationBuilder.ExecutionContext.IsPublishMode)
                {
                    server = server.WithRepl();
                }
                if (configuration.PersistentStorage) { server.WithDataVolume().WithLifetime(ContainerLifetime.Persistent); }
                _database = server.AddDatabase("postgres-database", databaseName);
            }
            else
            {
                _connection = brain.ApplicationBuilder.AddParameter("postgres-connection", () =>
                    brain.ApplicationBuilder.Configuration["Parameters:postgres-connection"]
                    ?? brain.ApplicationBuilder.Configuration.GetConnectionString(configuration.ConnectionName)
                    ?? throw new InvalidOperationException($"Connection string '{configuration.ConnectionName}' is required."), secret: true)
                    .WithParentRelationship(module);
            }
            _configuration = configuration;
        }

        public override void Apply<TResource>(IResourceBuilder<TResource> builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            if (_configuration is null) { return; }
            if (_database is not null)
            {
                builder.WithReference(_database, connectionName: _configuration.ConnectionName)
                    .WithAnnotation(new WaitAnnotation(_database.Resource, WaitType.WaitUntilHealthy, exitCode: 0));
            }
            else if (_connection is not null)
            {
                builder.WithEnvironment($"ConnectionStrings__{_configuration.ConnectionName}", _connection);
            }
        }
    }
}
