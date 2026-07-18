using Orleans;
using System;
using System.Collections.Generic;

namespace DigitalBrain.Abstractions.Tasks;

[GenerateSerializer]
[Alias("DigitalBrain.Abstractions.Tasks.DurableTaskDetails")]
public sealed class DurableTaskDetails
{
    [Id(0)] public TaskStatusEnum Status { get; set; }
    [Id(1)] public string Result { get; set; } = string.Empty;
    [Id(2)] public string ErrorMessage { get; set; } = string.Empty;
    [Id(3)] public DateTime? ExpirationTimeUtc { get; set; }
    [Id(4)] public string TaskType { get; set; } = string.Empty;
    [Id(5)] public List<string> History { get; set; } = new();
}
