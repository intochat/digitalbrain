using DigitalBrain.Aspire.Hosting;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.ClickHouse.Aspire.Hosting;

public sealed class ClickHouseModuleHosting : IDigitalBrainModuleHosting
{
    public void Configure(DigitalBrainBuilder brain)
    {
        var options = brain.GetModuleConfiguration<ClickHouseModule>().GetSection(ClickHouseModuleOptions.SectionName).Get<ClickHouseModuleOptions>() ?? new();
        if (options.Hosting.Enabled)
        {
            new DigitalBrainModuleBuilder<ClickHouseModule>(brain).WithClickHouse(o =>
            {
                o.PersistentStorage = options.Hosting.PersistentStorage;
                o.AlwaysRunInitScripts = options.Hosting.AlwaysRunInitScripts;
                foreach (var seed in options.Hosting.Seeds) { o.WithSeed(seed); }
            });
        }
    }
}