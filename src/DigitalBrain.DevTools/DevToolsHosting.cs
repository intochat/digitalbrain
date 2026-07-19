using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;

namespace DigitalBrain;

public static class DevToolsHosting
{
    public static IApplicationBuilder UseDigitalBrainDevTools(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var environment = app.ApplicationServices.GetService(typeof(IHostEnvironment)) as IHostEnvironment;

        if (environment?.IsDevelopment() != true)
        {
            return app;
        }

        return app;
    }
}
