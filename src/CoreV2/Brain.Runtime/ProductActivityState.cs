using Brain.Runtime.Abstractions;

namespace Brain.Runtime;

[GenerateSerializer]
public sealed class ProductActivityState
{
    [Id(0)]
    public bool Initialized { get; set; }

    [Id(1)]
    public string OperationId { get; set; } = string.Empty;

    [Id(2)]
    public string InputJson { get; set; } = string.Empty;

    [Id(3)]
    public string InputHash { get; set; } = string.Empty;

    [Id(4)]
    public string Workspace { get; set; } = string.Empty;

    [Id(5)]
    public string Principal { get; set; } = string.Empty;

    [Id(6)]
    public string IdempotencyKey { get; set; } = string.Empty;

    [Id(7)]
    public RuntimeActivityStatus Status { get; set; }

    [Id(8)]
    public long Sequence { get; set; }

    [Id(9)]
    public string? ResultJson { get; set; }

    [Id(10)]
    public string? Problem { get; set; }
}
