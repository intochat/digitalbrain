using DigitalBrain.Core;
using Orleans.Hosting;

namespace DigitalBrain.Time;

public sealed class TimeModule : IModule
{
    public void Configure(ISiloBuilder silo) => silo.AddTime();
}
