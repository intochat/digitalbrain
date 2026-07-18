using Ino.Core;

namespace Ino.Kernel.Contracts;

[GenerateSerializer]
public sealed record EchoResponse(
    [property: Id(0)] string Message,
    [property: Id(1)] string? SiloAddress = null) : ISynapse;
