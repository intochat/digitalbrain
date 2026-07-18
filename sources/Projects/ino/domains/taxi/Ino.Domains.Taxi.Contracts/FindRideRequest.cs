using Ino.Core;

namespace Ino.Domains.Taxi.Contracts;

/// <summary>
/// User-initiated intent to book or estimate a ride. Scaffold-only for the
/// initial Taxi neuron — a later slice wires this to an Uber MCP server
/// (if one exists) with the user's Google auth token, or ships a mocked
/// provider for dev + demo. Fields are kept minimal until the integration
/// target is chosen: pickup + dropoff as free-text, the concrete shape
/// comes from whatever MCP tool schema the provider exposes.
/// </summary>
[GenerateSerializer]
public sealed record FindRideRequest(
    [property: Id(0)] string Pickup,
    [property: Id(1)] string Dropoff) : ISynapse;
