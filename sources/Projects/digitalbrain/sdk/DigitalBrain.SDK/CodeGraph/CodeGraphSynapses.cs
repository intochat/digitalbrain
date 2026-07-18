using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.CodeGraph;

[GenerateSerializer]
public enum CodeGraphQueryType
{
    Search = 0,
    Context = 1,
    Callers = 2,
    Callees = 3,
    Impact = 4
}

[GenerateSerializer]
public sealed record CodeGraphQueryRequest([property: Id(1)] CodeGraphQueryType QueryType,
    [property: Id(2)] string QueryText,
    [property: Id(3)] string? KindFilter = null,
    [property: Id(4)] int Limit = 50
) : Synapse;

[GenerateSerializer]
public sealed record CodeGraphQueryResponse([property: Id(1)] bool Success,
    [property: Id(2)] string? ErrorMessage,
    [property: Id(3)] IReadOnlyList<string> Columns,
    [property: Id(4)] IReadOnlyList<IReadOnlyList<string?>> Rows,
    [property: Id(5)] string? ResultJson = null
) : Synapse;
