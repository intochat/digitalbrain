namespace DigitalBrain.Identity.Local.Custody;

/// <summary>Holds each account's master key; callers hand it secrets to wrap and the master key never leaves it.</summary>
public interface IMasterKeyCustody
{
    /// <summary>Wraps <paramref name="secret"/> for <paramref name="accountId"/> bound to <paramref name="context"/>.</summary>
    byte[] Wrap(string accountId, string context, byte[] secret);

    /// <summary>Unwraps <paramref name="wrapped"/> for <paramref name="accountId"/> bound to <paramref name="context"/>.</summary>
    byte[] Unwrap(string accountId, string context, byte[] wrapped);
}
