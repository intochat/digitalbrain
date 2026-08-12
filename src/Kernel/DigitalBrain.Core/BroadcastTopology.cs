using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Core;

public sealed class BroadcastTopology(IReadOnlyCollection<BroadcastRoute> routes)
{
    public IReadOnlyCollection<BroadcastRoute> Routes { get; } = routes;
}

