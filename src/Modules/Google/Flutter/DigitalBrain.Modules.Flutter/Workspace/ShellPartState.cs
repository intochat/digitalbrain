using System.Security.Cryptography;
using Orleans;

namespace DigitalBrain.Flutter.Workspace;

[GenerateSerializer]
internal sealed class ShellPartState { [Id(0)] public string? Json { get; set; } }
