using Orleans;
using System;

namespace DigitalBrain.Abstractions.Tasks;

[GenerateSerializer]
[Alias("DigitalBrain.Abstractions.Tasks.DurableTaskRetryPolicy")]
public sealed class DurableTaskRetryPolicy
{
    [Id(0)] public int MaxAttempts { get; set; } = 3;
    [Id(1)] public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(2);
    [Id(2)] public double BackoffMultiplier { get; set; } = 2.0;
    [Id(3)] public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(30);
}
