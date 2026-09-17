using System.Text.Json;
using DigitalBrain.Abstractions.Behavior;


namespace DigitalBrain.Core.Behavior;

[GenerateSerializer]
internal sealed record BehaviorCatalogState([property: Id(0)] IReadOnlyList<string> Names);
