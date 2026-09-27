using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using DigitalBrain.Microsoft.DotNet;
using Microsoft.Extensions.Options;
using Orleans.Configuration;

namespace DigitalBrain.Microsoft.CSharp;

internal sealed record CSharpContainerState(CSharpFileStatus Status, int? ExitCode, DateTimeOffset? StartedAt);

internal sealed class DockerCSharpRunner(IProcessRunner processes, IOptions<CSharpOptions> options,
    IOptions<EndpointOptions> endpoint, IOptions<ClusterOptions> cluster)
{
    internal const string Image = "mcr.microsoft.com/dotnet/sdk:11.0.100-rc.1";
    internal const string ClientProject = SourceMount + "/src/Modules/DigitalBrain/Kernel/Client/DigitalBrain.Client.csproj";
    internal const string SourceMount = "/brain";
    private const string WorkMount = "/work";
    private const string PackagesVolume = "digitalbrain-csharp-nuget";
    // Docker Desktop and Linux engines (via --add-host host-gateway) both route this name to the host.
    private const string DockerHost = "host.docker.internal";
    private const string GracefulStopSeconds = "10";
    // Rides out a silo restart, yet lets a script that does not compile settle as Exited.
    private const int MaximumRestarts = 5;
    private static readonly TimeSpan DockerTimeout = TimeSpan.FromMinutes(2);

    private const string BuildProps = $"""
        <Project>
          <PropertyGroup>
            <PublishAot>false</PublishAot>
            <Nullable>enable</Nullable>
            <ImplicitUsings>enable</ImplicitUsings>
          </PropertyGroup>
          <ItemGroup>
            <ProjectReference Include="{ClientProject}" />
            <Using Include="DigitalBrain.Contracts" />
            <Using Include="DigitalBrain.Client" />
          </ItemGroup>
        </Project>
        """;

    private string Root => options.Value.Root!;

    public async Task StartAsync(string fileId, string source, IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken)
    {
        var workDirectory = Path.Combine(Root, ContainerName(fileId));
        Directory.CreateDirectory(workDirectory);
        await File.WriteAllTextAsync(Path.Combine(workDirectory, "app.cs"), source, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(workDirectory, "Directory.Build.props"), BuildProps, cancellationToken).ConfigureAwait(false);
        await StopAsync(fileId, cancellationToken).ConfigureAwait(false);
        var run = await DockerAsync(RunArguments(fileId, workDirectory, settings), cancellationToken).ConfigureAwait(false);
        if (run.ExitCode != 0) { throw new InvalidOperationException("docker run failed: " + run.Error.Trim()); }
    }

    // A killed script never unsubscribes, and every neuron it watched then waits on a dead observer.
    // Stopping first lets the script's host dispose its subscriptions on SIGTERM.
    public async Task StopAsync(string fileId, CancellationToken cancellationToken)
    {
        var container = ContainerName(fileId);
        await DockerAsync(["stop", "--time", GracefulStopSeconds, container], cancellationToken).ConfigureAwait(false);
        var removed = await DockerAsync(["rm", "--force", container], cancellationToken).ConfigureAwait(false);
        if (removed.ExitCode != 0 && !removed.Error.Contains("No such container", StringComparison.OrdinalIgnoreCase))
        { throw new InvalidOperationException("docker rm failed: " + removed.Error.Trim()); }
    }

    public async Task<CSharpContainerState> InspectAsync(string fileId, CancellationToken cancellationToken)
    {
        var inspect = await DockerAsync(["inspect", "--format", "{{.State.Status}}|{{.State.ExitCode}}|{{.State.StartedAt}}", ContainerName(fileId)], cancellationToken).ConfigureAwait(false);
        return inspect.ExitCode == 0 ? ParseState(inspect.Output) : new(CSharpFileStatus.Stopped, null, null);
    }

    public async Task<string> LogsAsync(string fileId, int tail, CancellationToken cancellationToken)
    {
        var logs = await DockerAsync(["logs", "--tail", Math.Clamp(tail, 1, 5000).ToString(CultureInfo.InvariantCulture), ContainerName(fileId)], cancellationToken).ConfigureAwait(false);
        return logs.ExitCode == 0 ? logs.Output + logs.Error : "";
    }

    internal static CSharpContainerState ParseState(string inspected)
    {
        var parts = inspected.Trim().Split('|');
        var status = parts[0] switch
        {
            "running" => CSharpFileStatus.Running,
            "restarting" => CSharpFileStatus.Restarting,
            "exited" or "dead" => CSharpFileStatus.Exited,
            _ => CSharpFileStatus.Stopped,
        };
        int? exitCode = status == CSharpFileStatus.Exited && parts.Length > 1 && int.TryParse(parts[1], CultureInfo.InvariantCulture, out var code) ? code : null;
        DateTimeOffset? startedAt = parts.Length > 2 && DateTimeOffset.TryParse(parts[2], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var started)
            && started.Year > 1 ? started : null;
        return new(status, exitCode, startedAt);
    }

    internal static string ContainerName(string fileId)
        => "csharp-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(fileId)))[..24];

    private List<string> RunArguments(string fileId, string workDirectory, IReadOnlyDictionary<string, string> settings)
    {
        var sourceRoot = options.Value.SourceRoot is { Length: > 0 } configured
            ? configured
            : throw new InvalidOperationException("CSharp SourceRoot is not configured; the container cannot mount the DigitalBrain sources.");
        var advertised = endpoint.Value.AdvertisedIPAddress ?? IPAddress.Loopback;
        List<string> arguments =
        [
            "run", "--detach", "--name", ContainerName(fileId),
            "--label", "digitalbrain.csharp=" + cluster.Value.ServiceId,
            "--restart", "on-failure:" + MaximumRestarts,
            "--add-host", DockerHost + ":host-gateway",
            "--volume", sourceRoot + ":" + SourceMount + ":ro",
            "--volume", workDirectory + ":" + WorkMount,
            "--volume", PackagesVolume + ":/root/.nuget/packages",
            "--workdir", WorkMount,
            "--env", "Gateways=" + new UriBuilder("gwy.tcp", advertised.ToString(), endpoint.Value.GatewayPort, "0").Uri,
            "--env", "ClusterId=" + cluster.Value.ClusterId,
            "--env", "ServiceId=" + cluster.Value.ServiceId,
        ];
        // Orleans addresses the silo by its advertised IP; a container reaches a loopback silo only through a relay.
        if (IPAddress.IsLoopback(advertised)) { arguments.AddRange(["--env", "GatewayRelayHost=" + DockerHost]); }
        foreach (var (name, value) in settings.OrderBy(setting => setting.Key, StringComparer.Ordinal))
        { arguments.AddRange(["--env", "CSharpFile__Settings__" + name + "=" + value]); }
        arguments.AddRange([Image, "dotnet", "run", "app.cs", "-p:ArtifactsPath=" + WorkMount + "/artifacts"]);
        return arguments;
    }

    private async Task<ProcessResult> DockerAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Root);
        var result = await processes.RunAsync("docker", arguments, Root, DockerTimeout, cancellationToken).ConfigureAwait(false);
        if (result.TimedOut) { throw new TimeoutException($"docker {arguments[0]} timed out after {DockerTimeout}."); }
        return result;
    }
}
