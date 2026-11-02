namespace DigitalBrain.Capabilities.Storage.Confinement;

/// <summary>Why a path was refused. The message is fixed and never includes the path.</summary>
public enum ConfinementReason
{
    Empty,
    Control,
    Unc,
    Device,
    Ads,
    Name,
    HardDeny,
    Outside,
    LinkEscape,
    TooLong,
}

/// <summary>A path is outside the allowed roots or on the hard-deny list. The message is always the fixed text.</summary>
public sealed class ConfinementDenied : UnauthorizedAccessException
{
    public const string FixedMessage = "That location is not available.";

    public ConfinementDenied(ConfinementReason reason)
        : base(FixedMessage)
    {
        Reason = reason;
    }

    public ConfinementReason Reason { get; }
}
