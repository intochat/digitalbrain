using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DigitalBrain.AI.WebSearch;

internal static class WebSearchHosting
{
    internal static void Add(IServiceCollection services, IConfiguration configuration)
        => Add(services, AIOptions.Read(configuration));

    internal static void Add(IServiceCollection services, AIOptions configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        if (!configuration.Tavily.Enabled)
        {
            return;
        }

        services.AddHttpClient(nameof(TavilyWebSearch)).AddTypedClient<IWebSearch>((http, sp) =>
            new TavilyWebSearch(http, sp.GetRequiredService<IOptions<AIOptions>>()));
    }
}
