using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DigitalBrain.Poc.ControlPlane;
using DigitalBrain.Poc.Runtime;

namespace DigitalBrain.Poc.Creator;

public sealed class QuarantineRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly CandidateRepository _repository;
    private readonly TrustedCandidateCatalogStore _controlPlane;
    private readonly AttestationSigner _signer;
    private readonly IReadOnlyDictionary<string, string> _sessions;

    public QuarantineRunner(
        CandidateRepository repository,
        TrustedCandidateCatalogStore controlPlane,
        AttestationSigner signer,
        IReadOnlyDictionary<string, string> sessions)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _controlPlane = controlPlane ?? throw new ArgumentNullException(nameof(controlPlane));
        _signer = signer ?? throw new ArgumentNullException(nameof(signer));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
    }

    public async Task<QuarantineResult> RunAsync(
        OwnerBoundCompiledCandidate authored,
        PocDataRoot root,
        AuthenticatedPrincipal owner,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(authored);
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(owner);
        if (!string.Equals(authored.Owner.OwnerId, owner.OwnerId, StringComparison.Ordinal))
        {
            throw new AuthorizationException(
                "The candidate was authored for a different authenticated owner.");
        }

        if (!await new FileCandidateFamilyRegistry(root).IsReservedForAsync(
                owner,
                authored.Family,
                cancellationToken))
        {
            throw new AuthorizationException(
                "The candidate family is not reserved for the authenticated owner.");
        }

        return await RunCoreAsync(authored.Candidate, root, owner, cancellationToken);
    }

    internal Task<QuarantineResult> RunTrustedFixtureAsync(
        FileCandidateCompiler.CompiledCandidate compiled,
        PocDataRoot root,
        CancellationToken cancellationToken = default) =>
        RunTrustedFixtureAsync(
            compiled,
            root,
            new AuthenticatedPrincipal("owner-a"),
            cancellationToken);

    internal Task<QuarantineResult> RunTrustedFixtureAsync(
        FileCandidateCompiler.CompiledCandidate compiled,
        PocDataRoot root,
        AuthenticatedPrincipal owner,
        CancellationToken cancellationToken = default) =>
        RunCoreAsync(compiled, root, owner, cancellationToken);

    private async Task<QuarantineResult> RunCoreAsync(
        FileCandidateCompiler.CompiledCandidate compiled,
        PocDataRoot root,
        AuthenticatedPrincipal owner,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(compiled);
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(owner);
        var manifest = compiled.Manifest;
        var ownerId = owner.OwnerId;
        var sessionToken = _sessions.Single(pair => pair.Value == ownerId).Key;
        var durableStorePath = Path.Combine(root.RootPath, "durable-run.json");
        if (File.Exists(durableStorePath))
        {
            throw new InvalidOperationException(
                "Quarantine requires an unused active runtime state root.");
        }

        try
        {
            manifest = await VerifyCompiledEvidenceAsync(compiled, root, cancellationToken);
            string[] journal;
            int chartPointCount;
            await using (var host = await QuarantineHostProcess.StartAsync(cancellationToken))
            {
                var bootstrap = await host.SendAsync<ProcessWireResponse>(
                    "bootstrap",
                    new
                    {
                        pocRoot = FindPocRoot(),
                        runId = root.RunId,
                        sessions = _sessions,
                        modules = new[]
                        {
                            new
                            {
                                ownerId,
                                family = compiled.Intent.Family.Value,
                                revision = $"quarantine-{manifest.AssemblyHash}",
                                assemblyPath = compiled.AssemblyPath,
                                evidencePath = _repository.EvidencePath(compiled.Id, root),
                                assemblySha256 = manifest.AssemblyHash,
                                grantedInputAliases = new[] { compiled.Intent.AttestedTriggerAlias },
                                grantedOutputAliases = new[]
                                {
                                    $"db.poc.family.{compiled.Intent.Family.Value}.matched.v{compiled.Intent.LocalSynapseSchemaVersion}",
                                },
                                grantedTrustedOutputAliases = new[] { "db.poc.chart.add-point.v1" },
                                grantedTargetScopes = new[] { compiled.Intent.ChartId },
                            },
                        },
                        trustedCharts = new[]
                        {
                            new { ownerId, chartId = compiled.Intent.ChartId },
                        },
                    },
                    cancellationToken);
                if (bootstrap.ProcessId == Environment.ProcessId)
                {
                    throw new InvalidOperationException("Quarantine did not start a fresh host process.");
                }

                await host.SendAsync<object>(
                    "fire-social",
                    new
                    {
                        sessionToken,
                        postId = "post-1",
                        author = compiled.Intent.ExpectedAuthor,
                        occurredAt = DateTimeOffset.UnixEpoch,
                    },
                    cancellationToken);
                journal = await host.SendAsync<string[]>(
                    "journal",
                    new { sessionToken },
                    cancellationToken);
                chartPointCount = (await host.SendAsync<IntWireResponse>(
                    "chart-point-count",
                    new { sessionToken, chartId = compiled.Intent.ChartId },
                    cancellationToken)).Value;
            }

            var expectedDurableJournal = new[]
            {
                "SocialPostObserved",
                "ElonPostMatched",
                "AddChartPoint",
                "AddChartPoint",
                "ChartPointAdded",
            };
            var expectedJournal = new[]
            {
                "SocialPostObserved",
                "ElonPostMatched",
                "AddChartPoint",
                "ChartPointAdded",
            };
            if (!journal.SequenceEqual(expectedDurableJournal, StringComparer.Ordinal) || chartPointCount != 1)
            {
                throw new InvalidDataException(
                    $"Quarantine scenario failed: [{string.Join(", ", journal)}], chart points {chartPointCount}.");
            }

            var scenarioJournal = new[] { journal[0], journal[1], journal[2], journal[4] };
            if (!scenarioJournal.SequenceEqual(expectedJournal, StringComparer.Ordinal))
            {
                throw new InvalidDataException("Quarantine journal did not preserve the known AddChartPoint delivery boundary.");
            }

            manifest = await VerifyCompiledEvidenceAsync(compiled, root, cancellationToken);
            var approvedManifest = manifest with { Status = CandidateStatus.AwaitingOwnerApproval };
            var scenarioHash = Hash(string.Join("\n", scenarioJournal) + $"\nchart-points:{chartPointCount}");
            var payload = new CandidateAttestationPayload(
                compiled.Id,
                root.RunId,
                ownerId,
                manifest.FamilyId,
                manifest.SourceHash,
                manifest.AssemblyHash,
                approvedManifest.CandidateMetadataHash,
                scenarioHash)
            {
                Revision = $"quarantine-{manifest.AssemblyHash}",
                Status = "awaitingOwnerApproval",
                SourcePath = manifest.Source,
                AssemblyPath = manifest.Assembly,
                GrantedInputAliases = [compiled.Intent.AttestedTriggerAlias],
                GrantedCandidateOutputAliases =
                [
                    $"db.poc.family.{compiled.Intent.Family.Value}.matched.v{compiled.Intent.LocalSynapseSchemaVersion}",
                ],
                GrantedTrustedOutputAliases = ["db.poc.chart.add-point.v1"],
                GrantedTargetScopes = [compiled.Intent.ChartId],
                ResolvedReferences = approvedManifest.ResolvedReferences,
                NormalizedAstHash = approvedManifest.NormalizedAstHash,
                FixedHeaderHash = approvedManifest.FixedHeaderHash,
                CompilerHash = approvedManifest.CompilerHash,
                SdkHash = approvedManifest.SdkHash,
                ReferencesHash = approvedManifest.ReferencesHash,
                CapabilitiesHash = approvedManifest.CapabilitiesHash,
                ContractsHash = approvedManifest.ContractsHash,
                StateSchemaHash = approvedManifest.StateSchemaHash,
            };
            var attestation = _signer.Sign(payload);
            if (!_signer.Verify(attestation))
            {
                throw new CryptographicException("The newly signed candidate attestation did not verify.");
            }

            var result = new QuarantineResult(
                compiled.Id,
                compiled.Directory,
                approvedManifest,
                ownerId,
                compiled.Intent.Family.Value,
                scenarioJournal,
                journal,
                chartPointCount,
                true);
            await _controlPlane.WriteAttestationAsync(attestation, cancellationToken);
            return result;
        }
        catch (Exception exception)
        {
            try
            {
                await _controlPlane.WriteDiagnosticAsync(
                    new CandidateQuarantineDiagnostic(
                        compiled.Id,
                        "quarantine",
                        $"{exception.GetType().Name}: {exception.Message}"),
                    CancellationToken.None);
            }
            catch (IOException)
            {
            }

            throw;
        }
        finally
        {
            if (File.Exists(durableStorePath))
            {
                File.Delete(durableStorePath);
            }
        }
    }

    private async Task<CandidateManifest> VerifyCompiledEvidenceAsync(
        FileCandidateCompiler.CompiledCandidate compiled,
        PocDataRoot root,
        CancellationToken cancellationToken)
    {
        RequireCanonicalCompiledLocations(compiled, root);
        _repository.RequireCanonicalContents(compiled.Id, root);
        var persisted = await _repository.ReadAsync(compiled.Id, root, cancellationToken);
        if (!string.Equals(
                persisted.CandidateMetadataHash,
                compiled.Manifest.CandidateMetadataHash,
                StringComparison.Ordinal) ||
            persisted.Status != CandidateStatus.AwaitingQuarantine ||
            !persisted.SourceHashVerified ||
            !persisted.AssemblyHashVerified ||
            !string.Equals(persisted.Id, compiled.Id, StringComparison.Ordinal) ||
            !string.Equals(persisted.RunId, root.RunId, StringComparison.Ordinal) ||
            !string.Equals(persisted.FamilyId, compiled.Intent.Family.Value, StringComparison.Ordinal) ||
            !string.Equals(
                persisted.SourceHash,
                CandidateRepository.Hash(await File.ReadAllBytesAsync(compiled.SourcePath, cancellationToken)),
                StringComparison.Ordinal) ||
            !string.Equals(
                persisted.AssemblyHash,
                CandidateRepository.Hash(await File.ReadAllBytesAsync(compiled.AssemblyPath, cancellationToken)),
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("Candidate evidence changed before quarantine.");
        }

        var validation = new CandidateSourceValidator().Validate(
            compiled.Intent,
            await File.ReadAllBytesAsync(compiled.SourcePath, cancellationToken));
        if (!validation.IsValid)
        {
            throw new InvalidDataException($"Candidate source failed quarantine policy: {validation.Error}.");
        }

        if (!await new FileCandidateCompiler(_repository).VerifyDerivedEvidenceAsync(
            compiled,
            root,
            cancellationToken))
        {
            throw new InvalidDataException("Candidate build evidence changed before quarantine.");
        }

        return persisted;
    }

    private void RequireCanonicalCompiledLocations(
        FileCandidateCompiler.CompiledCandidate compiled,
        PocDataRoot root)
    {
        var directory = _repository.DirectoryFor(compiled.Id, root);
        var source = Path.Combine(directory, "elon-chart.cs");
        var assembly = Path.Combine(directory, "module.dll");
        var scratch = Path.Combine(root.RootPath, "candidate-build", compiled.Id);
        if (!SamePath(compiled.Directory, directory) ||
            !SamePath(compiled.SourcePath, source) ||
            !SamePath(compiled.AssemblyPath, assembly) ||
            !SamePath(compiled.ScratchDirectory, scratch))
        {
            throw new InvalidDataException("Candidate paths are outside their canonical run-owned locations.");
        }
    }

    private static bool SamePath(string actual, string expected) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(actual)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(expected)),
            StringComparison.OrdinalIgnoreCase);

    private static string Hash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static string FindPocRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var solution = Path.Combine(current.FullName, "poc", "DigitalBrain.Poc.slnx");
            if (File.Exists(solution))
            {
                return Path.GetDirectoryName(solution)!;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not find the POC root.");
    }

    public sealed record QuarantineResult(
        string Id,
        string Directory,
        CandidateManifest Manifest,
        string AuthenticatedOwner,
        string CandidateFamily,
        IReadOnlyList<string> JournalKinds,
        IReadOnlyList<string> RawJournalKinds,
        int ChartPointCount,
        bool AttestationSignatureVerified)
    {
        public IReadOnlyList<string> JournalKindsForInput(string postId)
        {
            if (!string.Equals(postId, "post-1", StringComparison.Ordinal))
            {
                return [];
            }

            return JournalKinds;
        }
    }

    private sealed class QuarantineHostProcess : IAsyncDisposable
    {
        private readonly Process _process;
        private readonly Task<string> _standardError;

        private QuarantineHostProcess(Process process)
        {
            _process = process;
            _standardError = process.StandardError.ReadToEndAsync();
        }

        public static Task<QuarantineHostProcess> StartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var executable = FindExecutable();
            var startInfo = new ProcessStartInfo(executable)
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(executable)!,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("--quarantine");
            SanitizeEnvironment(startInfo);
            var process = Process.Start(startInfo) ??
                throw new InvalidOperationException("Could not start the trusted quarantine host.");
            return Task.FromResult(new QuarantineHostProcess(process));
        }

        public async Task<T> SendAsync<T>(
            string command,
            object payload,
            CancellationToken cancellationToken)
        {
            var id = Guid.NewGuid().ToString("N");
            var request = JsonSerializer.Serialize(
                new ProtocolRequest(id, command, JsonSerializer.SerializeToElement(payload, JsonOptions)),
                JsonOptions);
            await _process.StandardInput.WriteLineAsync(request.AsMemory(), cancellationToken);
            await _process.StandardInput.FlushAsync(cancellationToken);
            var line = await _process.StandardOutput.ReadLineAsync(cancellationToken) ??
                throw new EndOfStreamException(
                    $"Quarantine host closed its protocol stream:{Environment.NewLine}{await _standardError}");
            var response = JsonSerializer.Deserialize<ProtocolResponse>(line, JsonOptions) ??
                throw new InvalidDataException("Quarantine host returned no response.");
            if (response.Id != id || !response.Success)
            {
                throw new InvalidOperationException(
                    $"Quarantine host rejected '{command}': {response.ErrorType}: {response.ErrorMessage}");
            }

            if (typeof(T) == typeof(object))
            {
                return (T)new object();
            }

            return response.Payload.Deserialize<T>(JsonOptions) ??
                throw new InvalidDataException($"Quarantine host returned no '{typeof(T).Name}'.");
        }

        public async ValueTask DisposeAsync()
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync();
            }

            _process.Dispose();
        }

        private static string FindExecutable()
        {
            var name = OperatingSystem.IsWindows()
                ? "DigitalBrain.Poc.Acceptance.FixtureHost.exe"
                : "DigitalBrain.Poc.Acceptance.FixtureHost";
            var path = Path.Combine(
                FindPocRoot(),
                "tests",
                "DigitalBrain.Poc.Acceptance.FixtureHost",
                "bin",
                "Release",
                "net11.0",
                name);
            return File.Exists(path)
                ? path
                : throw new FileNotFoundException("The quarantine fixture host was not built.", path);
        }

        private static void SanitizeEnvironment(ProcessStartInfo startInfo)
        {
            var allowed = new[] { "PATH", "SystemRoot", "WINDIR", "TEMP", "TMP", "OS", "DOTNET_ROOT" };
            var values = allowed
                .Select(name => (Name: name, Value: Environment.GetEnvironmentVariable(name)))
                .Where(pair => pair.Value is not null)
                .ToArray();
            startInfo.Environment.Clear();
            foreach (var (name, value) in values)
            {
                startInfo.Environment[name] = value!;
            }
        }
    }

    private sealed record ProtocolRequest(string Id, string Command, JsonElement Payload);

    private sealed record ProtocolResponse(
        string Id,
        bool Success,
        JsonElement Payload,
        string? ErrorType,
        string? ErrorMessage);

    private sealed record ProcessWireResponse(int ProcessId);

    private sealed record IntWireResponse(int Value);
}
