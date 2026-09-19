using DigitalBrain.Behaviors;
using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Hosting;

namespace IntoChat;

internal static class BehaviorEndpoints
{
    public static void AddBehaviors(this IHostApplicationBuilder builder)
    {
        builder.UseOrleansClient(client => client.AddDigitalBrain());
        builder.Services.TryAddSingleton<IDigitalBrain>(services => services.GetRequiredService<BrainClient>());
        builder.Services.AddBehavior<ElonBitcoin>();
    }

    public static void MapBehaviors(this IEndpointRouteBuilder _) { }
}
