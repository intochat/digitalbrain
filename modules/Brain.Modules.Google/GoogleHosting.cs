using Brain.Kernel;
using Brain.Kernel.Connections;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;

namespace Brain.Modules.Google;

public static class GoogleHosting
{
    public static ISiloBuilder AddBrainGoogle(this ISiloBuilder silo, IConfiguration config)
    {
        silo.AddBrainKind("gmail", sp => new GmailKind(sp.GetRequiredService<IGrainFactory>(), sp));

        var options = GoogleProviderOptions.FromConfiguration(config);
        if (options is null)
        {
            silo.Services.AddKeyedSingleton<IGmailProvider>("google", new DevGmailProvider());
            return silo;
        }

        silo.Services.AddHttpClient();
        silo.Services.AddKeyedSingleton<IConnectionProvider>("google", (sp, _) => new GoogleHttpProvider(sp.GetRequiredService<IHttpClientFactory>(), options));
        silo.Services.AddKeyedSingleton<IGmailProvider>("google", (sp, _) => new GoogleHttpProvider(sp.GetRequiredService<IHttpClientFactory>(), options));
        return silo;
    }
}
