using System.Text.Json;
using DigitalBrain.Abstractions.Behavior;


namespace DigitalBrain.Core.Behavior;

[GenerateSerializer]
internal sealed record BehaviorRunState(
    [property: Id(0)] BehaviorDefinition Definition,
    [property: Id(1)] BehaviorRunSnapshot Snapshot);
