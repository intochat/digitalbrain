using Microsoft.AspNetCore.Routing;

namespace DigitalBrain.Core;

public interface IModule
{
    void Configure(ISiloBuilder builder);

    void Configure(IEndpointRouteBuilder endpoints) { }
}
