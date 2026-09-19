using DigitalBrain.Behaviors;
using DigitalBrain.Contracts;
using DigitalBrain.Core;

namespace IntoChat;

internal static class BehaviorEndpoints
{
    public static void AddBehaviors(this IHostApplicationBuilder builder)
    {
        builder.Services.AddBehavior<ElonBitcoin>();
    }

    public static void MapBehaviors(this IEndpointRouteBuilder _) { }
}
