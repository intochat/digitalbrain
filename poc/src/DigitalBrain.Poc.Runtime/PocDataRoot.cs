using System.Runtime.ExceptionServices;

namespace DigitalBrain.Poc.Runtime;

public sealed class PocDataRoot : IAsyncDisposable
{
    private readonly string _pocRoot;
    private readonly IReadOnlyList<(string Parent, string Owned)> _ownedRoots;
    private bool _disposed;

    private PocDataRoot(string pocRoot, string runId)
    {
        _pocRoot = Resolve(pocRoot);
        RunId = runId;
        RootPath = Path.Combine(_pocRoot, "artifacts", runId);
        CandidateRoot = Path.Combine(_pocRoot, "candidates", runId);
        ControlPlaneRoot = Path.Combine(_pocRoot, "control-plane-store", runId);
        PointerLedgerAuthorityPath = Path.Combine(_pocRoot, "pointer-ledger-authority", runId);
        JournalPath = Path.Combine(RootPath, "journal");
        OutboxPath = Path.Combine(RootPath, "outbox");
        SnapshotPath = Path.Combine(RootPath, "snapshots");
        ChartProjectionPath = Path.Combine(RootPath, "chart-projections");
        TestSessionPath = Path.Combine(RootPath, "test-sessions");
        CandidateEvidencePath = Path.Combine(RootPath, "candidate-evidence");

        _ownedRoots =
        [
            (Path.Combine(_pocRoot, "artifacts"), RootPath),
            (Path.Combine(_pocRoot, "candidates"), CandidateRoot),
            (Path.Combine(_pocRoot, "control-plane-store"), ControlPlaneRoot),
            (Path.Combine(_pocRoot, "pointer-ledger-authority"), PointerLedgerAuthorityPath),
        ];

        foreach (var path in new[]
        {
            JournalPath,
            OutboxPath,
            SnapshotPath,
            ChartProjectionPath,
            TestSessionPath,
            CandidateEvidencePath,
            CandidateRoot,
            ControlPlaneRoot,
            PointerLedgerAuthorityPath,
        })
        {
            Directory.CreateDirectory(path);
        }
    }

    public string RunId { get; }

    public string RootPath { get; }

    public string CandidateRoot { get; }

    public string ControlPlaneRoot { get; }

    public string PointerLedgerAuthorityPath { get; }

    public string JournalPath { get; }

    public string OutboxPath { get; }

    public string SnapshotPath { get; }

    public string ChartProjectionPath { get; }

    public string TestSessionPath { get; }

    public string CandidateEvidencePath { get; }

    internal string StorePath => Path.Combine(RootPath, "durable-run.json");

    public static PocDataRoot Create(string pocRoot) =>
        new(pocRoot, $"run-{Guid.NewGuid():N}");

    public static PocDataRoot Open(string pocRoot, string runId)
    {
        ValidateRunId(runId);
        return new PocDataRoot(pocRoot, runId);
    }

    public static Task<IReadOnlyList<string>> FindArtifactsForRunAsync(
        string pocRoot,
        string runId,
        CancellationToken cancellationToken = default)
    {
        ValidateRunId(runId);
        cancellationToken.ThrowIfCancellationRequested();
        var resolvedPocRoot = Resolve(pocRoot);
        var matches = new List<string>();
        foreach (var parentName in new[]
        {
            "artifacts",
            "candidates",
            "control-plane-store",
            "pointer-ledger-authority",
        })
        {
            var parent = Path.Combine(resolvedPocRoot, parentName);
            if (!Directory.Exists(parent))
            {
                continue;
            }

            matches.AddRange(Directory.EnumerateFileSystemEntries(
                    parent,
                    "*",
                    SearchOption.AllDirectories)
                .Where(path => path.Contains(runId, StringComparison.Ordinal)));
            var direct = Path.Combine(parent, runId);
            if (Directory.Exists(direct) && !matches.Contains(direct, StringComparer.OrdinalIgnoreCase))
            {
                matches.Add(direct);
            }
        }

        return Task.FromResult<IReadOnlyList<string>>(matches);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Exception? deletionFailure = null;
        foreach (var (parent, owned) in _ownedRoots)
        {
            try
            {
                DeleteOwnedDirectory(parent, owned);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                deletionFailure ??= exception;
            }
        }

        var residual = await FindArtifactsForRunAsync(_pocRoot, RunId);
        if (residual.Count != 0)
        {
            throw new InvalidOperationException(
                "POC data-root disposal left run artifacts: " + string.Join(", ", residual),
                deletionFailure);
        }

        if (deletionFailure is not null)
        {
            ExceptionDispatchInfo.Capture(deletionFailure).Throw();
        }
    }

    private static void DeleteOwnedDirectory(string parent, string owned)
    {
        var resolvedParent = Resolve(parent);
        var resolvedOwned = Resolve(owned);
        if (resolvedOwned.Equals(resolvedParent, StringComparison.OrdinalIgnoreCase) ||
            !resolvedOwned.StartsWith(
                resolvedParent + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Refusing to remove non-owned POC path: {resolvedOwned}");
        }

        if (Directory.Exists(resolvedOwned))
        {
            foreach (var file in Directory.EnumerateFiles(resolvedOwned, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly);
            }

            Directory.Delete(resolvedOwned, recursive: true);
        }
    }

    private static string Resolve(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static void ValidateRunId(string runId)
    {
        if (!runId.StartsWith("run-", StringComparison.Ordinal) ||
            runId.Length != 36 ||
            runId[4..].Any(character => !Uri.IsHexDigit(character)))
        {
            throw new FormatException("A POC run identifier must use run- plus 32 hexadecimal characters.");
        }
    }
}
