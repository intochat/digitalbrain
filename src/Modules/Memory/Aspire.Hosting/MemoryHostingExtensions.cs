using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Memory.Qdrant;

namespace DigitalBrain.Memory.Aspire.Hosting;

public static class MemoryHostingExtensions
{
    public static DigitalBrainModuleBuilder<MemoryModule> WithQdrant(
        this DigitalBrainModuleBuilder<MemoryModule> module)
        => WithQdrant(module, null);

    public static DigitalBrainModuleBuilder<MemoryModule> WithQdrant(
        this DigitalBrainModuleBuilder<MemoryModule> module,
        Action<QdrantHostingOptions>? configure)
    {
        ArgumentNullException.ThrowIfNull(module);
        var options = new QdrantHostingOptions();
        configure?.Invoke(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ResourceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ConnectionName);
        State(module).Enable(options);
        return module;
    }

    private static MemoryHostingState State(DigitalBrainModuleBuilder<MemoryModule> module)
    {
        var state = module.DigitalBrainBuilder.GetOrAddState(brain => new MemoryHostingState(brain, module.Resource), out var added);
        if (added)
        {
            module.AddProjection(state);
        }

        return state;
    }

    private sealed class MemoryHostingState(
        DigitalBrainBuilder brain,
        IResourceBuilder<DigitalBrainModuleResource> module) : DigitalBrainModuleProjection
    {
        private IResourceBuilder<QdrantServerResource>? _qdrant;
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
            var qdrant = builder.AddQdrant(options.ResourceName).WithParentRelationship(module);
            if (options.PersistentStorage)
            {
                qdrant.WithDataVolume().WithLifetime(ContainerLifetime.Persistent);
            }
            _qdrant = qdrant;
            _enabled = true;
        }

        public override void Apply<TResource>(IResourceBuilder<TResource> builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            if (!_enabled || _qdrant is null)
            {
                return;
            }

            // WaitAnnotation: Apply targets IResourceWithEnvironment (kernel), not always IResourceWithWaitSupport.
            builder
                .WithReference(_qdrant, connectionName: _options.ConnectionName)
                .WithAnnotation(new WaitAnnotation(_qdrant.Resource, WaitType.WaitUntilHealthy, exitCode: 0))
                .WithEnvironment("DigitalBrain__Memory__Provider", MemoryModule.QdrantProviderName)
                .WithEnvironment(
                    "DigitalBrain__Memory__Qdrant__ConnectionName",
                    _options.ConnectionName);
            if (!string.IsNullOrWhiteSpace(_options.CollectionName))
            {
                builder.WithEnvironment("DigitalBrain__Memory__Qdrant__CollectionName", _options.CollectionName);
            }
        }
    }
}