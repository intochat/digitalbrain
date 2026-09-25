using System.Globalization;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Qdrant;

namespace DigitalBrain.Qdrant.Aspire.Hosting;

public static class QdrantHostingExtensions
{
    public static DigitalBrainModuleBuilder<QdrantModule> WithQdrant(
        this DigitalBrainModuleBuilder<QdrantModule> module,
        Action<QdrantHostingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(module);
        var options = new QdrantHostingOptions();
        configure?.Invoke(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ConnectionName);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.CollectionName);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.VectorSize);
        State(module).Enable(options);
        return module;
    }

    private static QdrantHostingState State(DigitalBrainModuleBuilder<QdrantModule> module)
    {
        var state = module.DigitalBrainBuilder.GetOrAddState(static brain => new QdrantHostingState(brain), out var added);
        if (added)
        {
            module.AddProjection(state);
        }

        return state;
    }

    private sealed class QdrantHostingState(DigitalBrainBuilder brain) : DigitalBrainModuleProjection
    {
        private IResourceBuilder<QdrantServerResource>? _server;
        private bool _enabled;
        private QdrantHostingOptions _options = new();

        internal void Enable(QdrantHostingOptions options)
        {
            if (_enabled)
            {
                return;
            }

            var builder = brain.ApplicationBuilder;
            _options = options;
            _server = builder.AddQdrant(QdrantNames.Server).WithParentRelationship(brain.Resource);
            if (options.PersistentStorage)
            {
                _server.WithDataVolume().WithLifetime(ContainerLifetime.Persistent);
            }

            _enabled = true;
        }

        public override void Apply<TResource>(IResourceBuilder<TResource> builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            if (!_enabled || _server is null)
            {
                return;
            }

            builder
                .WithReference(_server, connectionName: _options.ConnectionName)
                .WithAnnotation(new WaitAnnotation(_server.Resource, WaitType.WaitUntilHealthy, exitCode: 0))
                .WithEnvironment(EnvironmentKeys.For(QdrantModule.ConfigurationRoot, "Provider"), QdrantModule.ProviderName)
                .WithEnvironment(EnvironmentKeys.For(QdrantModule.ConfigurationRoot, "ConnectionName"), _options.ConnectionName)
                .WithEnvironment(EnvironmentKeys.For(QdrantModule.ConfigurationRoot, "CollectionName"), _options.CollectionName)
                .WithEnvironment(EnvironmentKeys.For(QdrantModule.ConfigurationRoot, "VectorSize"), _options.VectorSize.ToString(CultureInfo.InvariantCulture));
        }
    }
}
