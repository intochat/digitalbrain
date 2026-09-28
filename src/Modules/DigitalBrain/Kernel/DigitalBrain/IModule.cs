using Microsoft.AspNetCore.Routing;
using Orleans.Hosting;

namespace DigitalBrain.Core;

public interface IModule
{
    void Configure(ISiloBuilder silo);

    void Configure(IEndpointRouteBuilder endpoints) { }
}