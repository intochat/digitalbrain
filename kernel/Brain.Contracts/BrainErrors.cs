namespace Brain.Contracts;

public static class BrainErrors
{
    public const string UnknownKind = "kind.unknown";
    public const string UnknownContract = "contract.unknown";
    public const string RevisionConflict = "action.revision-stale";
    public const string Replayed = "action.replayed";
    public const string GrantMissing = "grant.missing";
    public const string EffectNotApproved = "effect.not-approved";
    public const string CallerMalformed = "caller.malformed";
    public const string ModelUnavailable = "model.unavailable";
    public const string ModelTimeout = "model.timeout";
    public const string ProviderTimeout = "provider.timeout";
    public const string ProviderError = "provider.error";
}

[GenerateSerializer, Alias("brain.exception.v2")]
public sealed class BrainException(string code, string detail) : Exception($"{code}: {detail}")
{
    [Id(0)]
    public string Code { get; } = code;
}
