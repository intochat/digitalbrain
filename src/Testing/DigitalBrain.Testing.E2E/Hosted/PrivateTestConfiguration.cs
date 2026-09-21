using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;

namespace DigitalBrain.Testing.E2E;

internal sealed class PrivateTestConfiguration : IAsyncDisposable
{
    private readonly string _directory;
    public string FilePath { get; }
    private PrivateTestConfiguration(string directory) { _directory = directory; FilePath = Path.Combine(directory, "settings.json"); }

    public static async Task<PrivateTestConfiguration> CreateAsync(IReadOnlyDictionary<string, string?> settings, CancellationToken ct)
    {
        var snapshot = new Dictionary<string, string?>(settings, StringComparer.OrdinalIgnoreCase);
        foreach (var key in snapshot.Keys)
        {
            if (string.IsNullOrWhiteSpace(key) || key.Contains("__", StringComparison.Ordinal)
                || key.StartsWith("Orleans:", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith("ConnectionStrings:", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith("DigitalBrain:Testing:", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith("DigitalBrain:Modules:", StringComparison.OrdinalIgnoreCase))
            { throw new ArgumentException("Private settings cannot override host-owned configuration."); }
        }
        var directory = Path.Combine(Path.GetTempPath(), "digitalbrain-test-" + Guid.NewGuid().ToString("N"));
        if (OperatingSystem.IsWindows())
        {
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!,
                FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(directory).Create(security);
        }
        else { Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
        var result = new PrivateTestConfiguration(directory);
        try
        {
            await File.WriteAllTextAsync(result.FilePath, JsonSerializer.Serialize(snapshot), ct).ConfigureAwait(false);
            return result;
        }
        catch { await result.DisposeAsync().ConfigureAwait(false); throw; }
    }

    public ValueTask DisposeAsync()
    {
        File.Delete(FilePath);
        if (Directory.Exists(_directory)) { Directory.Delete(_directory); }
        return ValueTask.CompletedTask;
    }
}
