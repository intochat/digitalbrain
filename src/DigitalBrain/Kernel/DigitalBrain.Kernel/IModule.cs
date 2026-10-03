using DigitalBrain.Contracts;
using Orleans.Hosting;

namespace DigitalBrain.Kernel;

public interface IModule
{
    void Configure(ISiloBuilder silo);
}

public interface IModule<TOptions> : IModule where TOptions : class, IModuleOptions, new();
