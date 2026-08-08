using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Memory.Qdrant;

namespace DigitalBrain.Memory.Aspire.Hosting;

public static class MemoryHostingExtensions
{
    public static DigitalBrainModuleBuilder<MemoryModule> WithQdrant(
        this DigitalBrainModuleBuilder<MemoryModule> module)
    {
        ArgumentNullException.ThrowIfNull(module);
        State(module).Enable();
        return module;
    }

    private static MemoryHostingState State(DigitalBrainModuleBuilder<MemoryModule> module)
    {
        var state = module.Brain.GetOrAddState(static brain => new MemoryHostingState(brain), out var added);
        if (added)
        {
            module.AddProjection(state);
        }

        return state;
    }

    private sealed class MemoryHostingState(DigitalBrainBuilder brain) : DigitalBrainModuleProjection
    {
        private IResourceBuilder<QdrantServerResource>? _qdrant;
        private bool _enabled;

        internal void Enable()
        {
            if (_enabled)
            {
                return;
            }

            var builder = brain.ApplicationBuilder;
            _qdrant = builder.AddQdrant("qdrant");
            _enabled = true;
        }

        public override void Apply<TResource>(IResourceBuilder<TResource> builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            if (!_enabled || _qdrant is null)
            {
                return;
            }

            builder
                .WithReference(_qdrant, connectionName: QdrantVectorMemoryRegistration.DefaultConnectionName)
                .WithAnnotation(new WaitAnnotation(_qdrant.Resource, WaitType.WaitUntilHealthy, exitCode: 0))
                .WithEnvironment("DigitalBrain__Memory__Provider", MemoryModule.QdrantProviderName)
                .WithEnvironment(
                    "DigitalBrain__Memory__Qdrant__ConnectionName",
                    QdrantVectorMemoryRegistration.DefaultConnectionName);
        }
    }
}
