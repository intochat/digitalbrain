using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
namespace DigitalBrain.Kernel.AspNetCore;
public static class ModuleEndpoints
{
    public static WebApplication MapDigitalBrainModules(this WebApplication app)
    {
        foreach (var module in app.Services.GetServices<IModule>())
        {
            if (module is IHttpModule http) { http.Configure(app); }
        }
        return app;
    }
}
