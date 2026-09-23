namespace DigitalBrain.Contracts.Enforcement;

public enum CallDenial
{
    NotAuthenticated = 0,
    UntrustedCaller = 1,
    OutsideWorkspace = 2,
    MissingGrant = 3,
    MissingAllowance = 4,
    LimitReached = 5,
    AppDisabled = 6,
}

[GenerateSerializer, Alias("enforcement.call-request")]
public sealed record CallRequest
{
    [Id(0)] public required CallerContext Caller { get; init; }
    [Id(1)] public required string TargetNeuron { get; init; }
    [Id(2)] public required string Operation { get; init; }
    [Id(3)] public IReadOnlyList<string> SemanticTypeIds { get; init; } = [];
    [Id(4)] public decimal EstimatedCompute { get; init; }
    [Id(5)] public bool HasSideEffects { get; init; }
}

[GenerateSerializer, Alias("enforcement.call-decision")]
public sealed record CallDecision
{
    [Id(0)] public bool Allowed { get; init; }
    [Id(1)] public CallDenial? Denial { get; init; }
    [Id(2)] public string? Explanation { get; init; }

    public static CallDecision Allow() => new() { Allowed = true };

    public static CallDecision Deny(CallDenial denial, string explanation) =>
        new() { Allowed = false, Denial = denial, Explanation = explanation };
}

// The one enforcement point: grants (P2), then allowances and limits (P3), then the broker gateway (P4).
public interface ICallFilter
{
    ValueTask<CallDecision> AuthorizeAsync(CallRequest request, CancellationToken cancellationToken = default);
}
