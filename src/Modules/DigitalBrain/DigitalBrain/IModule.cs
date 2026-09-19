using Orleans.Hosting;

namespace DigitalBrain.Core;

public interface IModule
{
    void Configure(ISiloBuilder silo);
}
