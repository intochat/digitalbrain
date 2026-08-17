using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Core;

public sealed record BroadcastRoute(string SynapseAlias, string HandlerGrainType);

