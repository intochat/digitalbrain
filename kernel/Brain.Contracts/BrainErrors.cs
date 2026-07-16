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
}

[GenerateSerializer, Alias("brain.exception.v2")]
public sealed class BrainException(string code, string detail) : Exception($"{code}: {detail}")
{
    [Id(0)]
    public string Code { get; } = code;
}
