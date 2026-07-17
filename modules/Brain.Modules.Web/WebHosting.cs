using Brain.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;

namespace Brain.Modules.Web;

public static class WebHosting
{
    public static ISiloBuilder AddBrainWeb(this ISiloBuilder silo)
    {
        silo.Services.AddHttpClient();
        return silo.AddBrainKind("web", sp => new WebKind(sp.GetRequiredService<IHttpClientFactory>()));
    }
}
