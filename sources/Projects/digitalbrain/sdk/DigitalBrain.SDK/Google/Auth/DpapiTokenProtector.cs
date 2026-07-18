using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace DigitalBrain.SDK.Google.Auth;

[SupportedOSPlatform("windows")]
internal sealed class DpapiTokenProtector : ITokenProtector
{
    public byte[] Protect(byte[] plaintext) =>
        ProtectedData.Protect(plaintext, optionalEntropy: null, DataProtectionScope.CurrentUser);

    public byte[] Unprotect(byte[] ciphertext) =>
        ProtectedData.Unprotect(ciphertext, optionalEntropy: null, DataProtectionScope.CurrentUser);
}
