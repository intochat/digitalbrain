using System.Security.Cryptography;
using System.Text;

namespace DigitalBrain.Hosting.Tray;

// DPAPI-backed key vault for LLM API keys, OAuth refresh tokens, and other
// long-lived secrets. Per docs/final-simplification/02-WINDOWS-AUTOSTART.md
// section 5 and the v6 decision log: ProtectedData.Protect with
// DataProtectionScope.CurrentUser. Per-user, per-machine: a key blob written
// on one machine cannot be decrypted on another (this is the design).
//
// R-003 acknowledges that admin-reset of a Windows password renders DPAPI
// blobs unreadable; v6 ships without recovery passphrase (v7).
internal static class KeyVault
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("digitalbrain.v6");

    public static string DefaultRoot =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DigitalBrain", "keys");

    private static string KeyPath(string purpose, string? root = null) =>
        Path.Combine(root ?? DefaultRoot, $"{purpose}.bin");

    public static void Store(string purpose, ReadOnlySpan<byte> secret, string? root = null)
    {
        var path = KeyPath(purpose, root);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var encrypted = ProtectedData.Protect(
            secret.ToArray(), Entropy, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(path, encrypted);
    }

    public static byte[]? Load(string purpose, string? root = null)
    {
        var path = KeyPath(purpose, root);
        if (!File.Exists(path)) return null;
        var encrypted = File.ReadAllBytes(path);
        return ProtectedData.Unprotect(
            encrypted, Entropy, DataProtectionScope.CurrentUser);
    }

    public static bool Delete(string purpose, string? root = null)
    {
        var path = KeyPath(purpose, root);
        if (!File.Exists(path)) return false;
        File.Delete(path);
        return true;
    }
}
