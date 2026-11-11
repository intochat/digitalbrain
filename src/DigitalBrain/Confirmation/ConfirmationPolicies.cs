namespace DigitalBrain.Confirmation;

/// <summary>
/// The per-brain confirmation policy preferences. Default <see cref="ConfirmationPolicy.DestructiveOnly"/>.
/// </summary>
public sealed class ConfirmationPolicies
{
    public ConfirmationPolicy DefaultPolicy { get; set; } = ConfirmationPolicy.DestructiveOnly;
    public Dictionary<string, ConfirmationPolicy> Brains { get; set; } = new(StringComparer.Ordinal);

    public ConfirmationPolicy For(string brain)
        => Brains.TryGetValue(brain, out var policy) && Enum.IsDefined(policy) ? policy : DefaultPolicy;
}
