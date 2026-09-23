using DigitalBrain.Apps;

namespace DigitalBrain.Broker;

// An egress rule ties a manifest data class to the hosts an app may reach while carrying it. A
// wildcard data class ("*") grants hosts to every call; host entries are suffix matches, either an
// exact host or "*.example.com"; "*" permits any host.
public sealed record EgressRule
{
    public const string AnyDataClass = "*";
    public const string AnyHost = "*";

    public required string DataClass { get; init; }
    public IReadOnlyList<string> AllowedHosts { get; init; } = [];
}

public sealed record EgressDecision
{
    public required bool Allowed { get; init; }
    public string? Explanation { get; init; }

    public static EgressDecision Allow() => new() { Allowed = true };

    public static EgressDecision Deny(string explanation) => new() { Allowed = false, Explanation = explanation };
}

public interface IEgressPolicy
{
    EgressDecision Evaluate(AppManifest manifest, IReadOnlyList<string> dataClasses, IReadOnlyList<string> hosts);
}
