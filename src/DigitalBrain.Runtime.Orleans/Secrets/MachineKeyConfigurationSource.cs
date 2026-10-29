using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Runtime.Orleans;

/// <summary>
/// Supplies <c>IntoChatOS:MasterKey</c> from <c>&lt;Home&gt;/secrets/master.key</c> when no explicit value exists. Mints a
/// 32-byte key (base64, a format os accepts) on first start, and refuses to mint while <c>&lt;Home&gt;/state</c> holds data.
/// </summary>
public sealed class MachineKeyConfigurationSource : IConfigurationSource
{
    /// <summary>The key file name under <c>&lt;Home&gt;/secrets</c>.</summary>
    public const string FileName = "master.key";

    /// <summary>Creates a source.</summary>
    /// <param name="home">Where the key and state live.</param>
    /// <param name="existing">The configuration built so far; an explicit master key there wins.</param>
    /// <param name="protector">null uses <see cref="MachineKeyProtectors.ForCurrentPlatform"/>.</param>
    public MachineKeyConfigurationSource(StateHome home, IConfiguration? existing, IMachineKeyProtector? protector = null)
    {
        Home = home ?? throw new ArgumentNullException(nameof(home));
        Existing = existing;
        Protector = protector ?? MachineKeyProtectors.ForCurrentPlatform();
    }

    /// <summary>The Home.</summary>
    public StateHome Home { get; }

    /// <summary>The configuration built so far.</summary>
    public IConfiguration? Existing { get; }

    /// <summary>The protector.</summary>
    public IMachineKeyProtector Protector { get; }

    /// <summary>The key file.</summary>
    public string KeyFile => Path.Combine(Home.Secrets, FileName);

    /// <inheritdoc />
    public IConfigurationProvider Build(IConfigurationBuilder builder) => new KeySource(this);

    /// <summary>Reads the key, minting it when none exists; null when an explicit key is configured.</summary>
    /// <exception cref="InvalidOperationException">The key file is missing while state holds data.</exception>
    public string? ResolveKey()
    {
        if (!string.IsNullOrWhiteSpace(Existing?[IntoChatOSNames.MasterKeyKey])) { return null; }
        if (File.Exists(KeyFile))
        {
            return Convert.ToBase64String(Protector.Unprotect(File.ReadAllBytes(KeyFile)));
        }
        if (Directory.Exists(Home.State) && Directory.EnumerateFiles(Home.State, "*", SearchOption.AllDirectories).Any())
        {
            throw new InvalidOperationException(
                $"{IntoChatOSNames.MasterKeyKey}: {FileName} is missing under {Home.Secrets} but {Home.State} holds data. "
                + "Restore the key file or set the master key explicitly; a new key would not open existing state.");
        }
        Home.EnsureCreated();
        var key = RandomNumberGenerator.GetBytes(32);
        Write(Protector.Protect(key));
        return Convert.ToBase64String(key);
    }

    private void Write(byte[] content)
    {
        var temp = KeyFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) { options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite; }
        try
        {
            using (var stream = new FileStream(temp, options))
            {
                stream.Write(content);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, KeyFile, overwrite: false);
        }
        finally
        {
            if (File.Exists(temp)) { File.Delete(temp); }
        }
    }

    private sealed class KeySource(MachineKeyConfigurationSource source) : ConfigurationProvider
    {
        public override void Load()
        {
            var key = source.ResolveKey();
            var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            if (key is not null) { data[IntoChatOSNames.MasterKeyKey] = key; }
            Data = data;
        }
    }
}
