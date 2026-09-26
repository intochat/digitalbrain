using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Qdrant;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Qdrant.Aspire.Hosting;

public sealed class QdrantModuleHosting : IDigitalBrainModuleHosting
{
    public void Configure(DigitalBrainBuilder brain)
    {
        var options = brain.GetModuleConfiguration<QdrantModule>().GetSection(QdrantModuleOptions.SectionName).Get<QdrantModuleOptions>() ?? new();
        if (!options.Hosting.Enabled)
        {
            return;
        }

        new DigitalBrainModuleBuilder<QdrantModule>(brain).WithQdrant(hosting =>
        {
            hosting.PersistentStorage = options.Hosting.PersistentStorage;
            hosting.ConnectionName = options.ConnectionName;
            hosting.CollectionName = options.CollectionName;
            hosting.VectorSize = options.VectorSize;
        });
    }
}