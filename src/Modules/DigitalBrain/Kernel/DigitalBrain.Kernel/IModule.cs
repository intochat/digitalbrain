using Microsoft.AspNetCore.Routing;
using Orleans.Hosting;

namespace DigitalBrain.Kernel;

public interface IModule
{
    void Configure(ISiloBuilder silo);

    void Configure(IEndpointRouteBuilder endpoints) { }
}

public interface IModule<TOptions> : IModule where TOptions : class, IModuleOptions, new();

public interface IModuleOptions
{
    void Validate();
}
