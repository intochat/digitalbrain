namespace DigitalBrain.Runtime.Orleans;

/// <summary>Stores the key as is; the protection is the file's owner-only mode (0600) inside the 0700 secrets folder.</summary>
public sealed class KeyFileMachineKeyProtector : IMachineKeyProtector
{
    /// <inheritdoc />
    public byte[] Protect(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return [.. key];
    }

    /// <inheritdoc />
    public byte[] Unprotect(byte[] stored)
    {
        ArgumentNullException.ThrowIfNull(stored);
        return [.. stored];
    }
}
