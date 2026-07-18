using DigitalBrain.Protocol.Domain.ValueObjects.Identity;
using System;
using System.Collections.Generic;

namespace DigitalBrain.Protocol.Domain.Events;

[GenerateSerializer]
public sealed record DynamicSynapse(
    [property: Id(0)] string TypeName,
    [property: Id(1)] Dictionary<string, string> Payload
) : Synapse;
