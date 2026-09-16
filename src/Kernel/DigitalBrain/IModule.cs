using Microsoft.AspNetCore.Routing;

namespace DigitalBrain.Core;

public interface IModule
{
    void Configure(ISiloBuilder builder);

    /// <summary>Map this module's HTTP endpoints. Endpoints use the host's owner gate by default.</summary>
    void Configure(IEndpointRouteBuilder endpoints) { }
}
