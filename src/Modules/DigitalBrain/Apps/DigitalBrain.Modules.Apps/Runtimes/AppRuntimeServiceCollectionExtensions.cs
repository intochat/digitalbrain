using DigitalBrain;
using DigitalBrain.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace DigitalBrain.Apps;

public static class AppRuntimeServiceCollectionExtensions
{
    public static IServiceCollection AddAppRuntime<TRuntime>(this IServiceCollection services) where TRuntime : class, IAppRuntime
        => services.AddSingleton<IAppRuntime, TRuntime>();
}
