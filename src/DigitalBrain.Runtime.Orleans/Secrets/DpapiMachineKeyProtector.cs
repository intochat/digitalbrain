using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace DigitalBrain.Runtime.Orleans;

/// <summary>Seals the key with DPAPI for the current Windows user.</summary>
public sealed class DpapiMachineKeyProtector : IMachineKeyProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("DigitalBrain.Runtime.Orleans/master.key");

    /// <inheritdoc />
    public byte[] Protect(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return OperatingSystem.IsWindows() ? Seal(key) : throw new PlatformNotSupportedException("DPAPI is available on Windows only.");
    }

    /// <inheritdoc />
    public byte[] Unprotect(byte[] stored)
    {
        ArgumentNullException.ThrowIfNull(stored);
        return OperatingSystem.IsWindows() ? Open(stored) : throw new PlatformNotSupportedException("DPAPI is available on Windows only.");
    }

    [SupportedOSPlatform("windows")]
    private static byte[] Seal(byte[] key) => ProtectedData.Protect(key, Entropy, DataProtectionScope.CurrentUser);

    [SupportedOSPlatform("windows")]
    private static byte[] Open(byte[] stored) => ProtectedData.Unprotect(stored, Entropy, DataProtectionScope.CurrentUser);
}
