using System.Diagnostics;

namespace DigitalBrain.Testing.E2E;

// Own this outside the server sessions. Failed rehearsals retain their data by default.
public sealed class DurableStorageVolume : IAsyncDisposable
{
    private bool _completed;
    private bool _disposed;
    private readonly bool _keepOnFailure;
    private DurableStorageVolume(string name, bool keepOnFailure)
    { Name = name; _keepOnFailure = keepOnFailure; }
    public string Name { get; }

    public static async Task<DurableStorageVolume> CreateAsync(bool keepOnFailure = true, CancellationToken cancellationToken = default)
    {
        var volume = new DurableStorageVolume("brain-e2e-" + Guid.NewGuid().ToString("N"), keepOnFailure);
        await RunAsync(cancellationToken, "volume", "create", volume.Name);
        return volume;
    }

    public void Complete() { ObjectDisposedException.ThrowIf(_disposed, this); _completed = true; }

    public async Task WaitUntilReleasedAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            while (true)
            {
                var containers = await RunAsync(deadline.Token, "ps", "-q", "--filter", "volume=" + Name);
                if (string.IsNullOrWhiteSpace(containers)) { return; }
                await Task.Delay(200, deadline.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new TimeoutException($"Storage volume '{Name}' still has a running writer."); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) { return; }
        if (!_completed && _keepOnFailure) { _disposed = true; return; }
        await WaitUntilReleasedAsync();
        // Never remove attached containers or force deletion. Ownership is limited to this volume.
        await RunAsync(CancellationToken.None, "volume", "rm", Name);
        _disposed = true;
    }

    private static async Task<string> RunAsync(CancellationToken cancellationToken, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("docker")
        { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in arguments) { startInfo.ArgumentList.Add(argument); }
        using var process = Process.Start(startInfo) ?? throw new IOException("Docker did not start.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync(deadline.Token); }
        catch
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw;
        }
        var stdout = await output;
        var stderr = await error;
        if (process.ExitCode != 0) { throw new IOException($"Docker exited with {process.ExitCode}: {stderr}"); }
        return stdout;
    }
}
