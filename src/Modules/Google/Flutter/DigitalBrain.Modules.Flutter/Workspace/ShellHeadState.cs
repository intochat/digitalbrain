using System.Security.Cryptography;
using Orleans;

namespace DigitalBrain.Flutter.Workspace;

[GenerateSerializer]
internal sealed class ShellHeadState
{
    [Id(0)] public long Revision { get; set; }
    [Id(1)] public string? Root { get; set; }
    // Id(2) retired; never reuse
    [Id(3)] public string? LastOperation { get; set; }
    [Id(4)] public string? LastDigest { get; set; }
}
