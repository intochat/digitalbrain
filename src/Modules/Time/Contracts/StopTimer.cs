using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Time;

/// <summary>A request to stop the scheduled timer.</summary>
[GenerateSerializer]
[Alias("time.stop-timer")]
public sealed record StopTimer(CommandId Id) : Command(Id);
