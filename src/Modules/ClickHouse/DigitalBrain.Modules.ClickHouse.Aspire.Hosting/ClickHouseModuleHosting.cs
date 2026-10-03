using DigitalBrain.Aspire.Hosting;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.ClickHouse.Aspire.Hosting;

public sealed class ClickHouseModuleHosting : IDigitalBrainModuleHosting
{
    public string Id => "clickhouse";
    public void Configure(DigitalBrainBuilder brain)
    {
        var options = brain.GetModuleConfiguration("clickhouse").GetModuleOptions<ClickHouseModuleOptions>("clickhouse");
        if (options.Hosting.Enabled)
        {
            new DigitalBrainModuleBuilder<DigitalBrain.ClickHouse.Aspire.Hosting.ClickHouseModuleHosting>(brain).WithClickHouse(o =>
            {
                o.PersistentStorage = options.Hosting.PersistentStorage;
                o.AlwaysRunInitScripts = options.Hosting.AlwaysRunInitScripts;
                foreach (var seed in options.Hosting.Seeds) { o.WithSeed(seed); }
            });
        }
    }
}
