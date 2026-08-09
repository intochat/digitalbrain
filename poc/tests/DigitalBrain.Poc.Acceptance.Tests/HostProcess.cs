using System.Diagnostics;
using System.Text.Json;
using DigitalBrain.Poc.Abstractions;
using DigitalBrain.Poc.Host;
using DigitalBrain.Poc.Runtime;

namespace DigitalBrain.Poc.Acceptance.Tests;

internal sealed class HostProcess : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly Process _process;
    private readonly SemaphoreSlim _protocolGate = new(1, 1);
    private readonly Task<string> _standardError;
    private readonly OwnerSession _readSession;
    private bool _terminated;

    private HostProcess(Process process, OwnerSession readSession)
    {
        _process = process;
        _readSession = readSession;
        _standardError = process.StandardError.ReadToEndAsync();
    }

    public int ProcessId => _process.Id;

    public static Task<HostProcess> StartAsync(
        PocDataRoot root,
        TestOwnerAuthority owners,
        CancellationToken cancellationToken = default) =>
        StartAsyncCore(root, owners, verifiedFixture: true, cancellationToken, []);

    public static async Task<HostProcess> StartVerifiedFixtureAsync(
        PocDataRoot root,
        TestOwnerAuthority owners,
        CancellationToken cancellationToken = default,
        params CandidateFixture[] candidates) =>
        await StartAsyncCore(root, owners, verifiedFixture: true, cancellationToken, candidates);

    private static async Task<HostProcess> StartAsyncCore(
        PocDataRoot root,
        TestOwnerAuthority owners,
        bool verifiedFixture,
        CancellationToken cancellationToken,
        CandidateFixture[] candidates)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(owners);
        var executable = FindHostExecutable(verifiedFixture);
        var startInfo = new ProcessStartInfo(executable)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            CreateNoWindow = true,
        };
        var process = Process.Start(startInfo) ??
            throw new InvalidOperationException("Could not start the POC host process.");
        var host = new HostProcess(process, owners.SessionFor("owner-a"));
        try
        {
            await host.SendAsync<object>(
                "bootstrap",
                new BootstrapWireRequest(
                    FindPocRoot(),
                    root.RunId,
                    owners.ExportSessions(),
                    candidates.Select(candidate => new CandidateModuleWire(
                        candidate.Module.OwnerId,
                        candidate.Family.Value,
                        candidate.Module.Revision,
                        candidate.Module.AssemblyPath,
                        candidate.Module.EvidencePath,
                        candidate.Module.AssemblySha256,
                        candidate.Module.GrantedInputAliases.ToArray(),
                        candidate.Module.GrantedOutputAliases.ToArray(),
                        candidate.Module.GrantedTrustedOutputAliases.ToArray(),
                        candidate.Module.GrantedTargetScopes.ToArray()))
                    .ToArray(),
                    candidates
                        .SelectMany(candidate => candidate.TrustedCharts)
                        .Distinct()
                        .Select(chart => new TrustedChartWire(chart.OwnerId, chart.ChartId))
                        .ToArray()),
                cancellationToken);
            return host;
        }
        catch
        {
            await host.DisposeAsync();
            throw;
        }
    }

    public Task FireTrustedAsync(
        OwnerSession session,
        Synapse synapse,
        CancellationToken cancellationToken = default)
    {
        var command = synapse.GetType().Name switch
        {
            nameof(IncrementAndEmit) => "increment",
            nameof(ThrowAfterStateAndEmit) => "throw",
            nameof(ReplaceProbeState) => "replace-state",
            nameof(ProbeIngress) => "probe",
            _ => throw new NotSupportedException(
                $"The fixed host scenario protocol does not support '{synapse.GetType().FullName}'."),
        };
        var receiptId = synapse is IReceiptIdentity identity
            ? identity.ReceiptId
            : synapse is ProbeIngress probe
                ? probe.Value
                : throw new InvalidOperationException("The test synapse has no durable receipt identity.");
        var value = synapse switch
        {
            ProbeIngress probeValue => probeValue.Value,
            ReplaceProbeState replacement => replacement.Value,
            _ => null,
        };
        return SendAsync<object>(
            command,
            new FireWireRequest(session.Token, receiptId, value),
            cancellationToken);
    }

    public Task StageTrustedBeforeDrainAsync(
        OwnerSession session,
        ProbeIngress input,
        CancellationToken cancellationToken = default) =>
        SendAsync<object>(
            "stage-probe",
            new FireWireRequest(session.Token, input.Value, input.Value),
            cancellationToken);

    public Task<HostSnapshot> ReadSnapshotAsync(CancellationToken cancellationToken = default) =>
        SendAsync<HostSnapshot>(
            "snapshot",
            new SessionWireRequest(_readSession.Token),
            cancellationToken);

    public Task<IReadOnlyList<string>> JournalKindsAsync(
        CancellationToken cancellationToken = default) =>
        SendAsync<IReadOnlyList<string>>(
            "journal",
            new SessionWireRequest(_readSession.Token),
            cancellationToken);

    public async Task<int> ReadHandledCountAsync(
        string contractAlias,
        CancellationToken cancellationToken = default) =>
        (await SendAsync<IntWireResponse>(
            "handled-count",
            new AliasWireRequest(_readSession.Token, contractAlias),
            cancellationToken)).Value;

    public async Task<int> ReadTurnCountAsync(
        CandidateFamilyId family,
        CancellationToken cancellationToken = default) =>
        (await SendAsync<IntWireResponse>(
            "turn-count",
            new FamilyWireRequest(_readSession.Token, family.Value),
            cancellationToken)).Value;

    public Task<PersistedCandidatePayloadView> ReadPersistedCandidatePayloadAsync(
        OwnerSession session,
        CancellationToken cancellationToken = default) =>
        SendAsync<PersistedCandidatePayloadView>(
            "persisted-candidate-payload",
            new SessionWireRequest(session.Token),
            cancellationToken);

    public async Task TerminateAsync()
    {
        if (_terminated)
        {
            return;
        }

        _terminated = true;
        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await TerminateAsync();
        _process.Dispose();
        _protocolGate.Dispose();
    }

    private async Task<T> SendAsync<T>(
        string command,
        object payload,
        CancellationToken cancellationToken)
    {
        await _protocolGate.WaitAsync(cancellationToken);
        try
        {
            if (_process.HasExited)
            {
                throw new InvalidOperationException(
                    $"POC host exited with code {_process.ExitCode}:{Environment.NewLine}{await _standardError}");
            }

            var id = Guid.NewGuid().ToString("N");
            var request = JsonSerializer.Serialize(
                new ScenarioWireRequest(id, command, JsonSerializer.SerializeToElement(payload, JsonOptions)),
                JsonOptions);
            await _process.StandardInput.WriteLineAsync(request.AsMemory(), cancellationToken);
            await _process.StandardInput.FlushAsync(cancellationToken);
            var line = await _process.StandardOutput.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                throw new EndOfStreamException(
                    $"POC host closed its protocol stream:{Environment.NewLine}{await _standardError}");
            }

            var response = JsonSerializer.Deserialize<ScenarioWireResponse>(line, JsonOptions) ??
                throw new InvalidDataException("POC host returned an empty protocol response.");
            if (response.Id != id)
            {
                throw new InvalidDataException("POC host response correlation does not match the request.");
            }

            if (!response.Success)
            {
                throw response.ErrorType switch
                {
                    nameof(AuthorizationException) => new AuthorizationException(response.ErrorMessage!),
                    nameof(CapabilityDeniedException) => new CapabilityDeniedException(response.ErrorMessage!),
                    nameof(StateTooLargeException) => new RemoteStateTooLargeException(response.ErrorMessage!),
                    nameof(ProbeFailureException) => new ProbeFailureException(response.ErrorMessage!),
                    _ => new InvalidOperationException(
                        $"Remote {response.ErrorType}: {response.ErrorMessage}"),
                };
            }

            if (typeof(T) == typeof(object))
            {
                return (T)new object();
            }

            return response.Payload.Deserialize<T>(JsonOptions) ??
                throw new InvalidDataException($"POC host returned no '{typeof(T).Name}' payload.");
        }
        finally
        {
            _protocolGate.Release();
        }
    }

    internal static string FindNormalHostExecutable() => FindHostExecutable(verifiedFixture: false);

    private static string FindHostExecutable(bool verifiedFixture)
    {
        var root = FindPocRoot();
        var projectName = verifiedFixture
            ? "DigitalBrain.Poc.Acceptance.FixtureHost"
            : "DigitalBrain.Poc.Host";
        var executableName = OperatingSystem.IsWindows()
            ? $"{projectName}.exe"
            : projectName;
        var path = Path.Combine(
            root,
            verifiedFixture ? "tests" : "src",
            projectName,
            "bin",
            "Release",
            "net11.0",
            executableName);
        return File.Exists(path)
            ? path
            : throw new FileNotFoundException("The POC host apphost was not built.", path);
    }

    internal static string FindPocRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "poc", "DigitalBrain.Poc.slnx");
            if (File.Exists(candidate))
            {
                return Path.GetDirectoryName(candidate)!;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not find the POC root.");
    }
}
