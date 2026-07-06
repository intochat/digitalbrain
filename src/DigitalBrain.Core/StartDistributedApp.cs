namespace DigitalBrain.Core;

// Command / event synapses for core system neurons (per v2 spec)
[GenerateSerializer]
[Alias("DigitalBrain.Core.StartDistributedApp")]
public record StartDistributedApp(string AppName) : Synapse(nameof(StartDistributedApp), DateTimeOffset.UtcNow);
