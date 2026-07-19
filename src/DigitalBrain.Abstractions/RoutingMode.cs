namespace DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.routing-mode")]
public enum RoutingMode
{
    PointToPoint,
    Broadcast,
}
