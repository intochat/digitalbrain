using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Runtime.Orleans;

/// <summary>Where local state lives: <c>IntoChatOS:Home</c>, or <c>~/.intochat</c> when unset (P2-D-13).</summary>
public sealed record StateHome
{
    /// <summary>The default folder name under the user folder.</summary>
    public const string DefaultDirectoryName = ".intochat";

    private StateHome(string path) => Path = path;

    /// <summary>The Home folder.</summary>
    public string Path { get; }

    /// <summary><c>&lt;Home&gt;/state</c>.</summary>
    public string State => System.IO.Path.Combine(Path, "state");

    /// <summary><c>&lt;Home&gt;/secrets</c>, owner-only.</summary>
    public string Secrets => System.IO.Path.Combine(Path, "secrets");

    /// <summary><c>&lt;Home&gt;/apps</c>.</summary>
    public string Apps => System.IO.Path.Combine(Path, "apps");

    /// <summary><c>&lt;Home&gt;/logs</c>.</summary>
    public string Logs => System.IO.Path.Combine(Path, "logs");

    /// <summary><c>&lt;Home&gt;/settings.json</c>.</summary>
    public string SettingsFile => System.IO.Path.Combine(Path, "settings.json");

    /// <summary>Resolves Home; a leading <c>~</c> expands to the user folder.</summary>
    /// <exception cref="InvalidOperationException">The value is relative or names an existing file. Names the key only.</exception>
    public static StateHome Resolve(IConfiguration? configuration)
    {
        var configured = configuration?[IntoChatOSNames.HomeKey];
        if (string.IsNullOrWhiteSpace(configured)) { return Default(); }
        configured = configured.Trim();
        if (configured == "~" || configured.StartsWith("~/", StringComparison.Ordinal) || configured.StartsWith("~\\", StringComparison.Ordinal))
        {
            configured = System.IO.Path.Combine(UserFolder(), configured.Length > 2 ? configured[2..] : string.Empty);
        }
        if (!System.IO.Path.IsPathFullyQualified(configured))
        {
            throw new InvalidOperationException($"{IntoChatOSNames.HomeKey} must be an absolute path.");
        }
        var full = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(configured));
        if (File.Exists(full))
        {
            throw new InvalidOperationException($"{IntoChatOSNames.HomeKey} names a file; it must be a folder.");
        }
        return new StateHome(full);
    }

    /// <summary><c>~/.intochat</c>.</summary>
    public static StateHome Default() => new(System.IO.Path.Combine(UserFolder(), DefaultDirectoryName));

    /// <summary>Creates state, secrets, apps and logs; secrets is readable only by the current user.</summary>
    public StateHome EnsureCreated()
    {
        Directory.CreateDirectory(State);
        Directory.CreateDirectory(Apps);
        Directory.CreateDirectory(Logs);
        if (OperatingSystem.IsWindows())
        {
            var info = Directory.CreateDirectory(Secrets);
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            info.SetAccessControl(security);
        }
        else
        {
            const UnixFileMode owner = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
            Directory.CreateDirectory(Secrets, owner);
            File.SetUnixFileMode(Secrets, owner);
        }
        return this;
    }

    private static string UserFolder() => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
}
