using Brain.Kernel;
using Brain.Kernel.Connections;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans.Hosting;

namespace Brain.Modules.Google;

public static class GoogleRuntimeExtensions
{
    public static ISiloBuilder AddDigitalBrainGoogle(
        this ISiloBuilder silo,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        silo.AddBrainKind("gmail", services =>
            new GmailKind(services.GetRequiredService<IGrainFactory>(), services));
        silo.AddBrainKind("gmail-assistant", services =>
            new GmailInboxSummaryKind(services.GetRequiredService<IGrainFactory>()));

        var options = GoogleOptions.FromConfiguration(configuration, environment);
        if (options is null)
        {
            if (environment.IsDevelopment() ||
                string.Equals(environment.EnvironmentName, "Test", StringComparison.OrdinalIgnoreCase))
            {
                var developmentProvider = new DevGmailProvider();
                silo.Services.AddKeyedSingleton<IConnectionProvider>("google", developmentProvider);
                silo.Services.AddKeyedSingleton<IGmailProvider>("google", developmentProvider);
            }

            return silo;
        }

        silo.Services.AddHttpClient();
        silo.Services.AddSingleton(services =>
            new GoogleHttpProvider(services.GetRequiredService<IHttpClientFactory>(), options));
        silo.Services.AddKeyedSingleton<IConnectionProvider>("google", (services, _) =>
            services.GetRequiredService<GoogleHttpProvider>());
        silo.Services.AddKeyedSingleton<IGmailProvider>("google", (services, _) =>
            services.GetRequiredService<GoogleHttpProvider>());
        return silo;
    }
}
