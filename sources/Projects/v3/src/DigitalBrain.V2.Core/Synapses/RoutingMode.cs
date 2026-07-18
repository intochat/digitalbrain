namespace DigitalBrain.V2.Core.Synapses;

// Routing is a property of the ACT of firing, carried in the header — not a synapse
// subtype. The same payload can be broadcast in one flow and replied point-to-point in
// another, so routing must stay orthogonal to type. See docs/02-ino-and-broadcast.md.
public enum RoutingMode
{
    Broadcast,
    PointToPoint
}
