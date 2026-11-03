using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace DigitalBrain.Identity.Local.Custody;

/// <summary>
/// File-backed master-key custody storing a random 32-byte root in <c>&lt;directory&gt;/master-key</c>,
/// created on first use with owner-only permissions.
/// </summary>
public sealed class FileMasterKeyCustody : IMasterKeyCustody
{
    private const string FileName = "master-key";
    private const int KeyLength = 32;

    private readonly Lock _gate = new();
    private readonly string _directory;
    private readonly string _keyFile;
    private byte[]? _root;

    /// <summary>Creates file custody rooted at <paramref name="directory"/>.</summary>
    public FileMasterKeyCustody(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = Path.GetFullPath(directory);
        _keyFile = Path.Combine(_directory, FileName);
    }

    /// <inheritdoc />
    public byte[] Wrap(string accountId, string context, byte[] secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(context);
        ArgumentNullException.ThrowIfNull(secret);
        return SegmentSeal.Close(Master(accountId), context, secret);
    }

    /// <inheritdoc />
    public byte[] Unwrap(string accountId, string context, byte[] wrapped)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(context);
        ArgumentNullException.ThrowIfNull(wrapped);
        return SegmentSeal.Open(Master(accountId), context, wrapped);
    }

    private byte[] Master(string accountId)
        => HKDF.DeriveKey(HashAlgorithmName.SHA256, Root(), KeyLength, info: Encoding.UTF8.GetBytes($"master|{accountId}"));

    private byte[] Root()
    {
        lock (_gate)
        {
            if (_root is not null) { return _root; }
            if (File.Exists(_keyFile))
            {
                return _root = ReadVerified(_keyFile);
            }

            EnsureOwnerDirectory(_directory);
            var created = RandomNumberGenerator.GetBytes(KeyLength);
            WriteOwnerFile(_keyFile, created);
            return _root = ReadVerified(_keyFile);
        }
    }

    private static byte[] ReadVerified(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length != KeyLength)
        {
            throw new CryptographicException($"The master key file '{path}' must be {KeyLength} bytes, found {bytes.Length}.");
        }
        return bytes;
    }

    private static void EnsureOwnerDirectory(string directory)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(directory);
        }
        else
        {
            const UnixFileMode owner = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
            Directory.CreateDirectory(directory, owner);
        }
    }

    private static void WriteOwnerFile(string keyFile, byte[] content)
    {
        var temp = keyFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        try
        {
            using (var stream = new FileStream(temp, options))
            {
                stream.Write(content);
                stream.Flush(flushToDisk: true);
            }

            if (OperatingSystem.IsWindows())
            {
                var user = WindowsIdentity.GetCurrent().User;
                if (user is not null)
                {
                    var security = new FileSecurity();
                    security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
                    security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl, AccessControlType.Allow));
                    new FileInfo(temp).SetAccessControl(security);
                }
            }
            else
            {
                File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            try
            {
                File.Move(temp, keyFile, overwrite: false);
            }
            catch (IOException) when (File.Exists(keyFile))
            {
                // Another writer created the root key file concurrently.
            }
        }
        finally
        {
            if (File.Exists(temp)) { File.Delete(temp); }
        }
    }
}
