using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace DigitalBrain.Runtime.Security;

[SupportedOSPlatform("windows")]
public sealed class DpapiNeuronStateProtector : INeuronStateProtector
{
    public byte[] Protect(byte[] plaintext) =>
        ProtectedData.Protect(plaintext, optionalEntropy: null, DataProtectionScope.CurrentUser);

    public byte[] Unprotect(byte[] ciphertext) =>
        ProtectedData.Unprotect(ciphertext, optionalEntropy: null, DataProtectionScope.CurrentUser);
}
