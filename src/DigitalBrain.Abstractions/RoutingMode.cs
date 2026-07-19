using Orleans;

namespace DigitalBrain;

[GenerateSerializer]
[Alias("db.routing-mode")]
public enum RoutingMode
{
    PointToPoint,
    Broadcast,
}
