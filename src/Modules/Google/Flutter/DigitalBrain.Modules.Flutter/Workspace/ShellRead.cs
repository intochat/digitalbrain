using System.Security.Cryptography;
using Orleans;

namespace DigitalBrain.Flutter.Workspace;

[GenerateSerializer]
internal sealed record ShellRead([property: Id(0)] long Revision, [property: Id(1)] string? Json);
