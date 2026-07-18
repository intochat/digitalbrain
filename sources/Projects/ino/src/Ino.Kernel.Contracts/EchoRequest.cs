using Ino.Core;

namespace Ino.Kernel.Contracts;

[GenerateSerializer]
public sealed record EchoRequest([property: Id(0)] string Message) : ISynapse;
