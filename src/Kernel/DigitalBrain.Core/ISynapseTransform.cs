using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using DigitalBrain.Abstractions;
using Orleans;

namespace DigitalBrain.Core;

public interface ISynapseTransform
{
    string Name { get; }

    Synapse Apply(Synapse synapse);
}

