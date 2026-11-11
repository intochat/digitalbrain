using Orleans;

namespace DigitalBrain.Runtime.Orleans.Access;

[GenerateSerializer, Immutable, Alias("db.access.caller")]
internal sealed class CallerWire
{
    public const byte ActorPerson = 0;
    public const byte ActorAgent = 1;
    public const byte ActorApp = 2;
    public const byte ActorModule = 3;
    public const byte ActorScheduler = 4;
    public const byte ActorKernel = 5;

    [Id(0)] public byte Actor;              // 0: Person, 1: Agent, 2: App, 3: Module, 4: Scheduler, 5: Kernel
    [Id(1)] public string? Principal;
    [Id(2)] public string? Account;
    [Id(3)] public string? Name;            // App key or Module ID
    [Id(4)] public string Brain = string.Empty;
    [Id(5)] public string? Conversation;
    [Id(6)] public string? Via;
    [Id(7)] public bool Inside;
}
