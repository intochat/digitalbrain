using DigitalBrain.Aspire.Hosting;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Memory.Aspire.Hosting;

public sealed class MemoryModuleHosting : IDigitalBrainModuleHosting
{
    public void Configure(DigitalBrainBuilder brain)
    {
        var options = brain.GetModuleConfiguration<MemoryModule>().GetSection(MemoryModuleOptions.SectionName).Get<MemoryModuleOptions>() ?? new();
        if (options.HostQdrant)
        {
            new DigitalBrainModuleBuilder<MemoryModule>(brain).WithQdrant(o =>
            {
                o.ConnectionName = options.Qdrant.ConnectionName;
                o.CollectionName = options.Qdrant.CollectionName;
            });
        }
    }
}
