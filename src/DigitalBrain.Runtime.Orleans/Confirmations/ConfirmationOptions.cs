using DigitalBrain.Confirmation;

namespace DigitalBrain.Runtime.Orleans.Confirmations;

/// <summary>
/// Host-owned policy, scoped by brain. App settings cannot change this policy.
/// </summary>
public sealed class ConfirmationOptions
{
    public const string Section = "DigitalBrain:Confirmations";
    public Dictionary<string, ConfirmationPolicy> Brains { get; set; } = new(StringComparer.Ordinal);

    public ConfirmationPolicy For(string brain)
        => Brains.TryGetValue(brain, out var policy) && Enum.IsDefined(policy) ? policy : ConfirmationPolicy.DestructiveOnly;
}

/// <summary>
/// The per-brain confirmation policy. Default <see cref="ConfirmationPolicy.DestructiveOnly"/>.
/// </summary>
public sealed class ConfirmationPolicyPreference
{
    public const string Section = ConfirmationOptions.Section;

    public ConfirmationPolicy Value { get; set; } = ConfirmationPolicy.DestructiveOnly;

    public Dictionary<string, ConfirmationPolicy> Brains { get; set; } = new(StringComparer.Ordinal);

    public ConfirmationPolicy For(string brain)
        => Brains.TryGetValue(brain, out var policy) && Enum.IsDefined(policy) ? policy : Value;

    public static ConfirmationPolicy For(ConfirmationOptions options, string brain)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.For(brain);
    }
}
