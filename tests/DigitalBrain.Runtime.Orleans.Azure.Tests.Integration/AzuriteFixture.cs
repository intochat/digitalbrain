using System.Diagnostics;
using System.Globalization;
using Azure.Storage.Blobs;

[assembly: AssemblyFixture(typeof(DigitalBrain.Runtime.Orleans.Azure.Tests.Integration.AzuriteFixture))]

namespace DigitalBrain.Runtime.Orleans.Azure.Tests.Integration;

/// <summary>How the Azurite facts get a storage emulator.</summary>
public enum AzuriteMode
{
    /// <summary>AZURITE_CONNECTION_STRING is set (CI service container, 03-09).</summary>
    UseConfigured,

    /// <summary>Start mcr.microsoft.com/azure-storage/azurite with Docker for this run.</summary>
    StartContainer,

    /// <summary>Locally without Docker: skip with the named reason.</summary>
    Skip,

    /// <summary>In CI without Azurite: fail, never skip.</summary>
    Fail,
}

/// <summary>
/// One Azurite for the assembly. AZURITE_CONNECTION_STRING wins; otherwise Docker starts the image on random loopback ports
/// and removes it on dispose. Locally without Docker the facts skip with <see cref="SkipReason"/>; with CI=true they fail.
/// </summary>
public sealed class AzuriteFixture : IAsyncLifetime
{
    public const string ConnectionVariable = "AZURITE_CONNECTION_STRING";
    public const string Image = "mcr.microsoft.com/azure-storage/azurite";
    public const string SkipReason = "Azurite unavailable: AZURITE_CONNECTION_STRING is unset and Docker is not available on this machine.";
    private const string AccountKey = "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==";

    private string? _container;
    private string? _failure;

    /// <summary>The connection string, or null when skipped.</summary>
    public string? ConnectionString { get; private set; }

    public AzuriteMode Mode { get; private set; }

    /// <summary>The fixture's decision, pure so the CI rule is a fact.</summary>
    public static AzuriteMode Decide(string? configured, bool ci, bool dockerAvailable)
        => !string.IsNullOrWhiteSpace(configured) ? AzuriteMode.UseConfigured
            : dockerAvailable ? AzuriteMode.StartContainer
            : ci ? AzuriteMode.Fail
            : AzuriteMode.Skip;

    /// <summary>The connection string for the facts; skips or fails per <see cref="Mode"/>.</summary>
    public string Require()
    {
        if (ConnectionString is { } value) { return value; }
        if (Mode == AzuriteMode.Skip) { Assert.Skip(SkipReason); }
        throw new InvalidOperationException(_failure ?? "Azurite is required in CI (CI=true) but is unreachable.");
    }

    public async ValueTask InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable(ConnectionVariable);
        var ci = string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase);
        Mode = Decide(configured, ci, string.IsNullOrWhiteSpace(configured) && await DockerAvailable());
        switch (Mode)
        {
            case AzuriteMode.UseConfigured:
                ConnectionString = configured;
                break;
            case AzuriteMode.StartContainer:
                try { ConnectionString = await Start(); }
                catch (Exception error) when (error is InvalidOperationException or TimeoutException or System.ComponentModel.Win32Exception)
                {
                    _failure = "Docker is available but Azurite did not start: " + error.Message;
                    if (!ci) { Mode = AzuriteMode.Skip; }
                }
                break;
            case AzuriteMode.Fail:
                _failure = "CI=true but AZURITE_CONNECTION_STRING is unset and Docker is unavailable.";
                break;
        }
        if (ConnectionString is not null) { await WaitReady(ConnectionString); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null) { await Run("docker", $"rm -f {_container}", TimeSpan.FromSeconds(30)); }
    }

    private async Task<string> Start()
    {
        var (code, output) = await Run("docker",
            $"run --rm -d -p 127.0.0.1::10000 -p 127.0.0.1::10001 -p 127.0.0.1::10002 {Image} azurite --blobHost 0.0.0.0 --queueHost 0.0.0.0 --tableHost 0.0.0.0 --skipApiVersionCheck --loose",
            TimeSpan.FromMinutes(5));
        if (code != 0) { throw new InvalidOperationException($"docker run exited {code}: {output.Trim()}"); }
        _container = output.Trim().Split('\n')[^1].Trim();
        var blob = await Port(10000);
        var queue = await Port(10001);
        var table = await Port(10002);
        return string.Create(CultureInfo.InvariantCulture,
            $"DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey={AccountKey};BlobEndpoint=http://127.0.0.1:{blob}/devstoreaccount1;QueueEndpoint=http://127.0.0.1:{queue}/devstoreaccount1;TableEndpoint=http://127.0.0.1:{table}/devstoreaccount1;");
    }

    private async Task<int> Port(int inner)
    {
        var (code, output) = await Run("docker", $"port {_container} {inner}/tcp", TimeSpan.FromSeconds(30));
        if (code != 0) { throw new InvalidOperationException($"docker port exited {code}."); }
        var line = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)[0].Trim();
        return int.Parse(line[(line.LastIndexOf(':') + 1)..], CultureInfo.InvariantCulture);
    }

    private static async Task WaitReady(string connection)
    {
        var client = new BlobServiceClient(connection);
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (true)
        {
            try
            {
                await client.GetPropertiesAsync();
                return;
            }
            catch (Exception) when (DateTime.UtcNow < deadline) { await Task.Delay(500); }
        }
    }

    private static async Task<bool> DockerAvailable()
    {
        try { return (await Run("docker", "info --format {{.ServerVersion}}", TimeSpan.FromSeconds(30))).Code == 0; }
        catch (System.ComponentModel.Win32Exception) { return false; }
        catch (TimeoutException) { return false; }
    }

    internal static async Task<(int Code, string Output)> Run(string file, string arguments, TimeSpan timeout)
    {
        using var process = Process.Start(new ProcessStartInfo(file, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        }) ?? throw new InvalidOperationException($"{file} did not start.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var cts = new CancellationTokenSource(timeout);
        try { await process.WaitForExitAsync(cts.Token); }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"{file} {arguments.Split(' ')[0]} timed out.");
        }
        var output = await stdout;
        var error = await stderr;
        // Docker writes image-pull progress to stderr even on success; only stdout contains the container id or port.
        return (process.ExitCode, process.ExitCode == 0 ? output : output + error);
    }
}
