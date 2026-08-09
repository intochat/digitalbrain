using System.Diagnostics;
using System.Text.Json;
using DigitalBrain.Poc.Host;
using DigitalBrain.Poc.Runtime;

namespace DigitalBrain.Poc.Acceptance.Tests;

internal sealed class PointerSelectedHostProcess : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly Process _process;
    private readonly Task<string> _standardError;
    private bool _disposed;

    private PointerSelectedHostProcess(
        Process process,
        Task<string> standardError,
        bool succeeded,
        IReadOnlyList<string> activeSourceHashes)
    {
        _process = process;
        _standardError = standardError;
        Succeeded = succeeded;
        ActiveSourceHashes = activeSourceHashes;
    }

    public bool Succeeded { get; }

    public int ProcessId => _process.Id;

    public IReadOnlyList<string> ActiveSourceHashes { get; }

    public Task<string> StandardErrorAsync() => _standardError;

    public async Task<string> SendRawAsync(string line, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(line);
        await _process.StandardInput.WriteLineAsync(line.AsMemory(), cancellationToken);
        await _process.StandardInput.FlushAsync(cancellationToken);
        return await _process.StandardOutput.ReadLineAsync(cancellationToken) ?? throw new EndOfStreamException(
            "The pointer-selected host exited before replying.");
    }

    public static async Task<PointerSelectedHostProcess> StartAsync(
        PocDataRoot root,
        TestOwnerAuthority owners,
        CancellationToken cancellationToken = default)
        => await StartAsync(root, owners, null, cancellationToken);

    internal static async Task<PointerSelectedHostProcess> StartAsync(
        PocDataRoot root,
        TestOwnerAuthority owners,
        Action<ProcessStartInfo>? configureStartInfo,
        CancellationToken cancellationToken = default) =>
        await StartCoreAsync(root, owners, configureStartInfo, null, cancellationToken);

    internal static async Task<PointerSelectedHostProcess> StartRaceFixtureAsync(
        PocDataRoot root,
        TestOwnerAuthority owners,
        string token,
        CancellationToken cancellationToken = default) =>
        await StartCoreAsync(root, owners, null, token, cancellationToken);

    private static async Task<PointerSelectedHostProcess> StartCoreAsync(
        PocDataRoot root,
        TestOwnerAuthority owners,
        Action<ProcessStartInfo>? configureStartInfo,
        string? raceToken,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(owners);
        var executable = Path.Combine(
            HostProcess.FindPocRoot(),
            raceToken is null ? "src" : "tests",
            raceToken is null
                ? "DigitalBrain.Poc.Host"
                : "DigitalBrain.Poc.Acceptance.FixtureHost",
            "bin",
            "Release",
            "net11.0",
            OperatingSystem.IsWindows()
                ? raceToken is null
                    ? "DigitalBrain.Poc.Host.exe"
                    : "DigitalBrain.Poc.Acceptance.FixtureHost.exe"
                : raceToken is null
                    ? "DigitalBrain.Poc.Host"
                    : "DigitalBrain.Poc.Acceptance.FixtureHost");
        var startInfo = new ProcessStartInfo(executable)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            CreateNoWindow = true,
        };
        if (raceToken is not null)
        {
            startInfo.ArgumentList.Add("--normal-race");
        }

        startInfo.ArgumentList.Add(root.RootPath);
        startInfo.ArgumentList.Add(root.ControlPlaneRoot);
        if (raceToken is not null)
        {
            startInfo.ArgumentList.Add(raceToken);
        }
        startInfo.Environment[ActiveHostBootstrap.AttestationKeyEnvironment] = owners.AttestationPublicKey;
        startInfo.Environment[ActiveHostBootstrap.ApprovalKeyEnvironment] = owners.ApprovalPublicKey;
        startInfo.Environment[ActiveHostBootstrap.PointerKeyEnvironment] = owners.PointerPublicKey;
        startInfo.Environment[ActiveHostBootstrap.SessionsEnvironment] = JsonSerializer.Serialize(
            owners.ExportSessions(),
            JsonOptions);
        configureStartInfo?.Invoke(startInfo);
        var process = Process.Start(startInfo) ??
            throw new InvalidOperationException("Could not start the pointer-selected normal host.");
        var standardError = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadLineAsync(cancellationToken).AsTask();
        var exited = process.WaitForExitAsync(cancellationToken);
        var completed = await Task.WhenAny(output, exited);
        if (completed == output && output.Result is { } line)
        {
            var ready = JsonSerializer.Deserialize<ActiveHostReadyWire>(line, JsonOptions);
            if (ready is not null && ready.ProcessId == process.Id)
            {
                return new PointerSelectedHostProcess(
                    process,
                    standardError,
                    succeeded: true,
                    ready.ActiveSourceHashes);
            }
        }

        if (!process.HasExited)
        {
            await process.WaitForExitAsync(cancellationToken);
        }

        return new PointerSelectedHostProcess(process, standardError, succeeded: false, []);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync();
        }

        _process.Dispose();
    }

    private sealed record ActiveHostReadyWire(int ProcessId, string[] ActiveSourceHashes);
}
