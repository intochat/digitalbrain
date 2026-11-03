namespace DigitalBrain.Identity.Local.Custody;

/// <summary>Where a host's master-key custody root lives.</summary>
public enum MasterKeyCustodyKind
{
    /// <summary>Derived from a configured root key.</summary>
    Configured = 0,

    /// <summary>Held in hardware (TPM).</summary>
    Tpm = 1,

    /// <summary>Stored in an owner-only file under the host's state directory.</summary>
    File = 2,
}
