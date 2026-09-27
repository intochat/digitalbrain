using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DigitalBrain.Microsoft.DotNet;
using Microsoft.Extensions.Options;

namespace DigitalBrain.Microsoft.CSharp;

internal sealed class DockerCSharpRunner(IProcessRunner processes, IOptions<CSharpOptions> options) : ICSharpRunner
{
    internal const string SourceMount = "/brain";
    internal const string WorkMount = "/work";
    internal const string PackagesVolume = "digitalbrain-csharp-nuget";
    internal const string ClientProject = SourceMount + "/src/Modules/DigitalBrain/Kernel/Client/DigitalBrain.Client.csproj";

    internal static readonly string BuildProps = $"""
        <Project>
          <PropertyGroup>
            <PublishAot>false</PublishAot>
            <Nullable>enable</Nullable>
            <ImplicitUsings>enable</ImplicitUsings>
          </PropertyGroup>
          <ItemGroup>
            <ProjectReference Include="{ClientProject}" />
            <Using Include="DigitalBrain.Contracts" />
            <Using Include="DigitalBrain.Core" />
          </ItemGroup>
        </Project>
        """;

    private CSharpOptions Settings => options.Value;

    public async Task StartAsync(string fileId, string source, IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken)
    {
        var sourceRoot = Settings.SourceRoot ?? throw new InvalidOperationException("CSharp SourceRoot is not configured.");
        var container = ContainerName(fileId);
        var workDirectory = Path.Combine(Settings.Root!, container);
        Directory.CreateDirectory(workDirectory);
        await File.WriteAllTextAsync(Path.Combine(workDirectory, "app.cs"), source, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(workDirectory, "Directory.Build.props"), BuildProps, cancellationToken).ConfigureAwait(false);
        await RemoveAsync(container, cancellationToken).ConfigureAwait(false);
        var run = await DockerAsync(RunArguments(fileId, container, sourceRoot, workDirectory, settings), cancellationToken).ConfigureAwait(false);
        if (run.ExitCode != 0) { throw new InvalidOperationException("docker run failed: " + run.Error.Trim()); }
    }

    public Task StopAsync(string fileId, CancellationToken cancellationToken) => RemoveAsync(ContainerName(fileId), cancellationToken);

    public async Task<CSharpContainerState> InspectAsync(string fileId, CancellationToken cancellationToken)
    {
        var inspect = await DockerAsync(["inspect", "--format", "{{.State.Status}}|{{.State.ExitCode}}|{{.State.StartedAt}}", ContainerName(fileId)], cancellationToken).ConfigureAwait(false);
        return inspect.ExitCode == 0 ? ParseState(inspect.Output) : CSharpContainerState.Missing;
    }

    public async Task<string> LogsAsync(string fileId, int tail, CancellationToken cancellationToken)
    {
        var logs = await DockerAsync(["logs", "--tail", Math.Clamp(tail, 1, 5000).ToString(CultureInfo.InvariantCulture), ContainerName(fileId)], cancellationToken).ConfigureAwait(false);
        return logs.ExitCode == 0 ? logs.Output + logs.Error : "";
    }

    internal IReadOnlyList<string> RunArguments(string fileId, string container, string sourceRoot, string workDirectory, IReadOnlyDictionary<string, string> settings)
    {
        List<string> arguments =
        [
            "run", "--detach", "--name", container,
            "--label", "digitalbrain.csharp=" + Settings.ServiceId,
            "--restart", "on-failure",
            "--add-host", "host.docker.internal:host-gateway",
            "--volume", sourceRoot + ":" + SourceMount + ":ro",
            "--volume", workDirectory + ":" + WorkMount,
            "--volume", PackagesVolume + ":/root/.nuget/packages",
            "--workdir", WorkMount,
            "--env", "Gateways=" + Settings.Gateways,
            "--env", "ClusterId=" + Settings.ClusterId,
            "--env", "ServiceId=" + Settings.ServiceId,
            "--env", "CSharpFile__Id=" + fileId,
        ];
        if (Settings.GatewayRelayHost is { Length: > 0 } relayHost) { arguments.AddRange(["--env", "GatewayRelayHost=" + relayHost]); }
        foreach (var (name, value) in settings.OrderBy(setting => setting.Key, StringComparer.Ordinal))
        { arguments.AddRange(["--env", "CSharpFile__Settings__" + name + "=" + value]); }
        arguments.AddRange([Settings.Image, "dotnet", "run", "app.cs", "-p:ArtifactsPath=" + WorkMount + "/artifacts"]);
        return arguments;
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

    private async Task RemoveAsync(string container, CancellationToken cancellationToken)
    {
        var removed = await DockerAsync(["rm", "--force", container], cancellationToken).ConfigureAwait(false);
        if (removed.ExitCode != 0 && !removed.Error.Contains("No such container", StringComparison.OrdinalIgnoreCase))
        { throw new InvalidOperationException("docker rm failed: " + removed.Error.Trim()); }
    }

    private async Task<ProcessResult> DockerAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Settings.Root!);
        var result = await processes.RunAsync(Settings.DockerPath, arguments, Settings.Root!, Settings.DockerTimeout, cancellationToken).ConfigureAwait(false);
        if (result.TimedOut) { throw new TimeoutException($"docker {arguments[0]} timed out after {Settings.DockerTimeout}."); }
        return result;
    }
}
