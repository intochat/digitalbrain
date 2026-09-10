using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.AI;

public static class NativeToolServiceCollectionExtensions
{
    public static IServiceCollection AddNativeTool(this IServiceCollection services, string name, Func<IServiceProvider, AIFunction> create)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(create);
        return services.AddSingleton<INativeToolContributor>(new DelegateNativeTool(name, create));
    }

    private sealed class DelegateNativeTool(string name, Func<IServiceProvider, AIFunction> create) : INativeToolContributor
    {
        public string Name => name;

        public AIFunction Create(IServiceProvider services) => create(services);
    }
}
