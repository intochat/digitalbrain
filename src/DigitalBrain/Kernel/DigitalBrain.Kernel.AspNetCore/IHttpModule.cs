using Microsoft.AspNetCore.Routing;

namespace DigitalBrain.Kernel.AspNetCore;

public interface IHttpModule
{
    void Configure(IEndpointRouteBuilder endpoints);
}
