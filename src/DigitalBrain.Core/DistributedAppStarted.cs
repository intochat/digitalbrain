namespace DigitalBrain.Core;

[GenerateSerializer]
[Alias("DigitalBrain.Core.DistributedAppStarted")]
public record DistributedAppStarted(string AppName, bool Success, string? Details = null) : Synapse(nameof(DistributedAppStarted), DateTimeOffset.UtcNow);
