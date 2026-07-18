using DigitalBrain.V2.Core.Synapses;

namespace Ping.Contracts;

// Input: someone says ping.
[GenerateSerializer]
public sealed record Ping([property: Id(0)] string From) : Synapse;

// Output: the echo, announced to the room.
[GenerateSerializer]
public sealed record Pong([property: Id(0)] string To) : Synapse;
