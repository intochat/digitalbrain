using Microsoft.AspNetCore.Routing;

namespace DigitalBrain.Runtime.Http.Surface;

public interface IHttpModule
{
    void Configure(IEndpointRouteBuilder endpoints);
}
