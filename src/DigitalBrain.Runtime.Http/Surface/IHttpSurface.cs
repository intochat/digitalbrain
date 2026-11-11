using DigitalBrain.Modules;
using Microsoft.AspNetCore.Builder;

namespace DigitalBrain.Runtime.Http.Surface;

// A module's slice of the host HTTP pipeline (browser callbacks, webhooks). Registered as a
// singleton from Module.Configure; the host maps every surface in one place, before its gates.
public interface IHttpSurface
{
    void Map(IApplicationBuilder app);
}
