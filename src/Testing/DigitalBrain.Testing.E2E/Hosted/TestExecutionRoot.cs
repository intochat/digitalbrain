namespace DigitalBrain.Testing.E2E;

/// <summary>
/// A per-run directory handed to the AppHost as configuration so behavior and coding locks
/// never overlap with the developer's persisted root. Owned by the session for cleanup.
/// </summary>
public sealed class TestExecutionRoot : IAsyncDisposable
{
    private TestExecutionRoot(string path, string configurationKey)
    {
        Path = path;
        ConfigurationKey = configurationKey;
    }

    public string Path { get; }
    public string ConfigurationKey { get; }
    public string Argument => $"{ConfigurationKey}={Path}";

    public static TestExecutionRoot Create(string configurationKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationKey);
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "digitalbrain-e2e-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return new TestExecutionRoot(path, configurationKey);
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            if (Directory.Exists(Path)) { Directory.Delete(Path, recursive: true); }
        }
        catch (IOException) { /* The host still holds a handle after its shutdown deadline. */ }
        catch (UnauthorizedAccessException) { /* The host still holds a handle after its shutdown deadline. */ }
        return ValueTask.CompletedTask;
    }
}