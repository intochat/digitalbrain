using System.Security.Cryptography;
using System.Text.Json;
using DigitalBrain.Poc.ControlPlane;
using DigitalBrain.Poc.Runtime;

namespace DigitalBrain.Poc.Host;

internal static class ActiveHostBootstrap
{
    internal const string AttestationKeyEnvironment = "DIGITALBRAIN_POC_ATTESTATION_PUBLIC_KEY";
    internal const string ApprovalKeyEnvironment = "DIGITALBRAIN_POC_APPROVAL_PUBLIC_KEY";
    internal const string PointerKeyEnvironment = "DIGITALBRAIN_POC_POINTER_PUBLIC_KEY";
    internal const string SessionsEnvironment = "DIGITALBRAIN_POC_OWNER_SESSIONS";
    internal const string PreflightCandidateEnvironment = "DIGITALBRAIN_POC_PREFLIGHT_CANDIDATE";
    internal const string PreflightExpectedHeadEnvironment = "DIGITALBRAIN_POC_PREFLIGHT_EXPECTED_HEAD";
    internal const string PreflightSelectionEnvironment = "DIGITALBRAIN_POC_PREFLIGHT_SELECTION";
    internal const string AuthorityDelegationEnvironment = "DIGITALBRAIN_POC_HOST_AUTHORITY_DELEGATION";
    internal const string TestFaultEnvironment = "DIGITALBRAIN_POC_TEST_FAULT";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task RunNormalAsync(
        TextReader input,
        TextWriter output,
        string stateRoot,
        string controlPlaneRoot,
        CancellationToken cancellationToken) =>
        await RunNormalCoreAsync(
            input,
            output,
            stateRoot,
            controlPlaneRoot,
            afterObservedSelection: null,
            cancellationToken);

    internal static async Task RunNormalForTestAsync(
        TextReader input,
        TextWriter output,
        string stateRoot,
        string controlPlaneRoot,
        Func<PocDataRoot, CancellationToken, Task> afterObservedSelection,
        CancellationToken cancellationToken) =>
        await RunNormalCoreAsync(
            input,
            output,
            stateRoot,
            controlPlaneRoot,
            afterObservedSelection ?? throw new ArgumentNullException(nameof(afterObservedSelection)),
            cancellationToken);

    private static async Task RunNormalCoreAsync(
        TextReader input,
        TextWriter output,
        string stateRoot,
        string controlPlaneRoot,
        Func<PocDataRoot, CancellationToken, Task>? afterObservedSelection,
        CancellationToken cancellationToken)
    {
        var root = OpenRoots(stateRoot, controlPlaneRoot);
        var store = CreateStore(root);
        var observedActive = await store.ReadAllVerifiedActiveCandidatesAsync(cancellationToken);
        if (observedActive.Count == 0)
        {
            throw new InvalidDataException("Normal boot found no verified active candidate pointer.");
        }

        var observedHeads = await ReadActiveHeadsAsync(store, observedActive, cancellationToken);
        if (afterObservedSelection is not null)
        {
            await afterObservedSelection(root, cancellationToken);
        }
        var delegation = VerifySupervisorDelegation(
            root,
            ReadExpectedHeadForDelegation(),
            observedActive.Select(record => record.SourceHash));
        await using var authority = await HostAuthorityLease.AcquireForActiveHostAsync(
            root,
            delegation is not null,
            delegation is null ? null : RequireEnvironment(HostAuthorityLease.ControlTokenEnvironment),
            cancellationToken);
        var active = await store.ReadAllVerifiedActiveCandidatesAsync(cancellationToken);
        if (!SameActiveSelection(observedActive, active))
        {
            throw new CryptographicException(
                "Normal boot observed a changed verified active selection while acquiring authority.");
        }

        var expectedHeadFound = delegation is null;

        foreach (var candidate in active)
        {
            var principal = new AuthenticatedPrincipal(candidate.OwnerId);
            var family = CandidateFamilyId.Parse(candidate.FamilyId);
            var pointer = await store.VerifyActivePointerAsync(
                principal,
                family,
                cancellationToken);
            if (!pointer.Succeeded)
            {
                throw new CryptographicException(
                    $"Normal boot pointer verification failed: {pointer.Failure}.");
            }

            if (!string.Equals(
                    pointer.Pointer!.CandidateSourceHash,
                    candidate.SourceHash,
                    StringComparison.Ordinal))
            {
                throw new CryptographicException(
                    "Normal boot observed a pointer that no longer selects its verified candidate.");
            }

            if (!observedHeads.TryGetValue(SelectionKey(candidate), out var observedHead) ||
                CandidatePointerHead.From(pointer.Pointer) != observedHead)
            {
                throw new CryptographicException(
                    "Normal boot observed a changed verified pointer head while acquiring authority.");
            }

            if (delegation is not null &&
                CandidatePointerHead.From(pointer.Pointer) == delegation.ExpectedHead)
            {
                expectedHeadFound = true;
            }

            if (!IsZeroHash(pointer.Pointer!.PreviousCandidateSourceHash) &&
                await new CandidateSchemaCompatibility(root).HasRetainedFamilyJournalAsync(
                    principal,
                    family,
                    cancellationToken))
            {
                var previous = await store.ReadVerifiedCandidateForActivationAsync(
                    pointer.Pointer.PreviousCandidateSourceHash,
                    cancellationToken) ?? throw new CryptographicException(
                        "Normal boot cannot verify the previous schema retained by the family journal.");
                if (!string.Equals(
                    previous.StateSchemaHash,
                    candidate.StateSchemaHash,
                    StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "Normal boot refused an active pointer incompatible with the retained family journal.");
                }
            }
        }

        if (!expectedHeadFound)
        {
            throw new CryptographicException(
                "The supervisor delegation does not match a verified active pointer head.");
        }

        await HostScenarioProtocol.RunTrustedActiveAsync(
            input,
            output,
            root,
            active,
            ReadSessions(),
            authority,
            allowTestFaults: false,
            cancellationToken);
    }

    public static async Task<int> RunCandidatePreflightAsync(
        TextReader input,
        TextWriter output,
        string stateRoot,
        string controlPlaneRoot,
        CancellationToken cancellationToken)
    {
        var candidateId = RequireEnvironment(PreflightCandidateEnvironment).ToLowerInvariant();
        var expectedHead = JsonSerializer.Deserialize<CandidatePointerHead>(
            RequireEnvironment(PreflightExpectedHeadEnvironment),
            JsonOptions) ?? throw new InvalidDataException("Candidate preflight expected head is empty.");
        var expectedActiveSourceHashes = JsonSerializer.Deserialize<string[]>(
            RequireEnvironment(PreflightSelectionEnvironment),
            JsonOptions) ?? throw new InvalidDataException("Candidate preflight selection is empty.");
        PocDataRoot root;
        TrustedCandidateCatalogStore store;
        TrustedCandidateRecord candidate;
        IReadOnlyList<TrustedCandidateRecord> preflightCandidates;
        SupervisorDelegation? delegation = null;
        try
        {
            root = OpenRoots(stateRoot, controlPlaneRoot);
            store = CreateStore(root);
            candidate = await store.ReadVerifiedCandidateForActivationAsync(
                candidateId,
                cancellationToken) ?? throw new CryptographicException(
                    "Candidate preflight requires complete external attestation and owner approval evidence.");
            var principal = new AuthenticatedPrincipal(candidate.OwnerId);
            var family = CandidateFamilyId.Parse(candidate.FamilyId);
            var currentSnapshot = await store.ReadVerifiedPointerSnapshotAsync(
                principal,
                family,
                cancellationToken);
            if (currentSnapshot.Head != expectedHead)
            {
                throw new CryptographicException(
                    "The existing active pointer no longer matches the supervisor's verified head.");
            }

            if (currentSnapshot.Pointer is not null)
            {
                var current = await store.ReadVerifiedCandidateForActivationAsync(
                    currentSnapshot.Pointer.CandidateSourceHash,
                    cancellationToken) ?? throw new CryptographicException(
                        "The current active candidate is no longer verified.");
                if (!string.Equals(
                        current.StateSchemaHash,
                        candidate.StateSchemaHash,
                        StringComparison.Ordinal) &&
                    await new CandidateSchemaCompatibility(root).HasRetainedFamilyJournalAsync(
                        principal,
                        family,
                        cancellationToken))
                {
                    throw new InvalidDataException(
                        "The proposed local schema is incompatible with the retained family journal.");
                }
            }

            preflightCandidates = await ReadPreflightCandidatesAsync(
                store,
                expectedActiveSourceHashes,
                candidateId,
                cancellationToken);

            delegation = VerifySupervisorDelegation(
                root,
                expectedHead,
                preflightCandidates.Select(record => record.SourceHash));

            new CandidateAssemblyLoader().VerifyTrustedActive(
                root,
                preflightCandidates.Select(record => ToModule(root, record)).ToArray());

            await WritePreflightAsync(
                output,
                new CandidatePreflightResult(
                    true,
                    Environment.ProcessId,
                    candidate.OwnerId,
                    candidate.FamilyId,
                    candidate.SourceHash,
                    preflightCandidates.Select(record => record.SourceHash).ToArray(),
                    null),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await WritePreflightAsync(
                output,
                new CandidatePreflightResult(
                    false,
                    Environment.ProcessId,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    [],
                    exception.Message),
                cancellationToken);
            return 3;
        }

        var activation = await input.ReadLineAsync(cancellationToken);
        if (!string.Equals(activation, "activate", StringComparison.Ordinal))
        {
            return 4;
        }

        if (string.Equals(
            Environment.GetEnvironmentVariable(TestFaultEnvironment),
            HostFault.AfterPointerAdvanceBeforeActivation.ToString(),
            StringComparison.Ordinal))
        {
            return 6;
        }

        var verifiedPointer = await store.VerifyActivePointerAsync(
            new AuthenticatedPrincipal(candidate.OwnerId),
            CandidateFamilyId.Parse(candidate.FamilyId),
            cancellationToken);
        if (!verifiedPointer.Succeeded ||
            !string.Equals(
                verifiedPointer.Pointer!.CandidateSourceHash,
                candidate.SourceHash,
                StringComparison.Ordinal))
        {
            return 5;
        }

        var active = await store.ReadAllVerifiedActiveCandidatesAsync(cancellationToken);
        if (!SameSourceHashes(
            active.Select(record => record.SourceHash),
            expectedActiveSourceHashes))
        {
            return 7;
        }

        await using var authority = await HostAuthorityLease.AcquireForActiveHostAsync(
            root,
            delegation is not null,
            delegation is null ? null : RequireEnvironment(HostAuthorityLease.ControlTokenEnvironment),
            cancellationToken);
        await HostScenarioProtocol.RunTrustedActiveAsync(
            input,
            output,
            root,
            active,
            ReadSessions(),
            authority,
            allowTestFaults: true,
            cancellationToken);
        return 0;
    }

    private static PocDataRoot OpenRoots(string stateRoot, string controlPlaneRoot)
    {
        var resolvedState = Path.TrimEndingDirectorySeparator(Path.GetFullPath(stateRoot));
        var resolvedControlPlane = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(controlPlaneRoot));
        var runId = Path.GetFileName(resolvedState);
        var artifacts = Directory.GetParent(resolvedState) ??
            throw new InvalidDataException("The state root has no artifacts parent.");
        var pocRoot = artifacts.Parent?.FullName ??
            throw new InvalidDataException("The state root has no POC parent.");
        var root = PocDataRoot.Open(pocRoot, runId);
        if (!string.Equals(root.RootPath, resolvedState, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                root.ControlPlaneRoot,
                resolvedControlPlane,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new AuthorizationException(
                "The supplied state and trusted control-plane roots do not identify the same run.");
        }

        return root;
    }

    private static TrustedCandidateCatalogStore CreateStore(PocDataRoot root) =>
        new(
            root,
            VerifyOnlyAttestations(),
            VerifyOnlyApprovals(),
            VerifyOnlyPointers());

    private static async Task<IReadOnlyList<TrustedCandidateRecord>> ReadPreflightCandidatesAsync(
        TrustedCandidateCatalogStore store,
        IReadOnlyList<string> expectedActiveSourceHashes,
        string candidateId,
        CancellationToken cancellationToken)
    {
        if (!expectedActiveSourceHashes.Contains(candidateId, StringComparer.Ordinal) ||
            expectedActiveSourceHashes.Count == 0 ||
            expectedActiveSourceHashes.Any(value =>
                value.Length != 64 ||
                value.Any(character =>
                    !((character is >= '0' and <= '9') ||
                      (character is >= 'a' and <= 'f')))))
        {
            throw new CryptographicException("Candidate preflight selection is not canonical.");
        }

        var records = new List<TrustedCandidateRecord>();
        foreach (var sourceHash in expectedActiveSourceHashes.Distinct(StringComparer.Ordinal))
        {
            var record = await store.ReadVerifiedCandidateForActivationAsync(
                sourceHash,
                cancellationToken) ?? throw new CryptographicException(
                    "Candidate preflight selection contains incomplete signed evidence or approval.");
            records.Add(record);
        }

        if (records.Count != expectedActiveSourceHashes.Count)
        {
            throw new CryptographicException("Candidate preflight selection repeats a source identity.");
        }

        return records;
    }

    private static async Task<IReadOnlyDictionary<string, CandidatePointerHead>> ReadActiveHeadsAsync(
        TrustedCandidateCatalogStore store,
        IReadOnlyList<TrustedCandidateRecord> active,
        CancellationToken cancellationToken)
    {
        var heads = new Dictionary<string, CandidatePointerHead>(StringComparer.Ordinal);
        foreach (var candidate in active)
        {
            var snapshot = await store.ReadVerifiedPointerSnapshotAsync(
                new AuthenticatedPrincipal(candidate.OwnerId),
                CandidateFamilyId.Parse(candidate.FamilyId),
                cancellationToken);
            if (snapshot.Pointer is null ||
                !string.Equals(
                    snapshot.Pointer.CandidateSourceHash,
                    candidate.SourceHash,
                    StringComparison.Ordinal))
            {
                throw new CryptographicException(
                    "Normal boot did not receive a verified pointer for its active candidate.");
            }

            heads.Add(SelectionKey(candidate), snapshot.Head);
        }

        return heads;
    }

    private static VerifiedCandidateModule ToModule(
        PocDataRoot root,
        TrustedCandidateRecord candidate) =>
        new(
            candidate.OwnerId,
            CandidateFamilyId.Parse(candidate.FamilyId),
            candidate.SourceHash.ToLowerInvariant(),
            Path.Combine(root.CandidateRoot, candidate.CandidateId, candidate.AssemblyPath),
            Path.Combine(root.CandidateRoot, candidate.CandidateId, "candidate.json"),
            candidate.AssemblyHash.ToLowerInvariant(),
            candidate.GrantedInputAliases,
            candidate.GrantedCandidateOutputAliases,
            candidate.GrantedTrustedOutputAliases,
            candidate.GrantedTargetScopes);

    private static AttestationSigner VerifyOnlyAttestations() =>
        new(SigningForbidden, Convert.FromBase64String(RequireEnvironment(AttestationKeyEnvironment)));

    private static OwnerApprovalSigner VerifyOnlyApprovals() =>
        new(SigningForbidden, Convert.FromBase64String(RequireEnvironment(ApprovalKeyEnvironment)));

    private static PointerSigner VerifyOnlyPointers() =>
        new(SigningForbidden, Convert.FromBase64String(RequireEnvironment(PointerKeyEnvironment)));

    private static CandidatePointerHead? ReadExpectedHeadForDelegation()
    {
        if (string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable(AuthorityDelegationEnvironment)))
        {
            return null;
        }

        return JsonSerializer.Deserialize<CandidatePointerHead>(
            RequireEnvironment(PreflightExpectedHeadEnvironment),
            JsonOptions) ?? throw new InvalidDataException(
            "A supervisor delegation requires its expected pointer head.");
    }

    private static SupervisorDelegation? VerifySupervisorDelegation(
        PocDataRoot root,
        CandidatePointerHead? expectedHead,
        IEnumerable<string> activeSourceHashes)
    {
        var serialized = Environment.GetEnvironmentVariable(AuthorityDelegationEnvironment);
        if (string.IsNullOrWhiteSpace(serialized))
        {
            return null;
        }

        if (expectedHead is null)
        {
            throw new CryptographicException(
                "A supervisor delegation did not bind an expected pointer head.");
        }

        var delegation = JsonSerializer.Deserialize<HostAuthorityDelegation>(serialized, JsonOptions) ??
            throw new CryptographicException("The supervisor delegation is empty.");
        if (!VerifyOnlyPointers().VerifyHostAuthorityDelegation(delegation) ||
            !string.Equals(delegation.Payload.RunId, root.RunId, StringComparison.Ordinal) ||
            !string.Equals(
                delegation.Payload.ExpectedHeadPayloadHash,
                expectedHead.CurrentPayloadHash,
                StringComparison.Ordinal) ||
            !string.Equals(
                delegation.Payload.ActiveSelectionHash,
                PointerSigner.ActiveSelectionHash(activeSourceHashes),
                StringComparison.Ordinal))
        {
            throw new CryptographicException("The supervisor delegation is not authenticated for this selection.");
        }

        return new SupervisorDelegation(expectedHead);
    }

    private static byte[] SigningForbidden(byte[] _) =>
        throw new CryptographicException("A child host has verification authority only.");

    private static IReadOnlyDictionary<string, string> ReadSessions() =>
        JsonSerializer.Deserialize<IReadOnlyDictionary<string, string>>(
            RequireEnvironment(SessionsEnvironment),
            JsonOptions) ?? throw new InvalidDataException("The owner session set is empty.");

    private static string RequireEnvironment(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new AuthorizationException($"Required trusted host setting '{name}' is missing.");

    private static bool IsZeroHash(string value) =>
        string.Equals(value, new string('0', 64), StringComparison.Ordinal);

    private static bool SameSourceHashes(IEnumerable<string> left, IEnumerable<string> right) =>
        left.Select(value => value.ToLowerInvariant())
            .Order(StringComparer.Ordinal)
            .SequenceEqual(
                right.Select(value => value.ToLowerInvariant()).Order(StringComparer.Ordinal),
                StringComparer.Ordinal);

    private static bool SameActiveSelection(
        IEnumerable<TrustedCandidateRecord> left,
        IEnumerable<TrustedCandidateRecord> right) =>
        left.Select(candidate => SelectionKey(candidate) + "\n" + candidate.SourceHash.ToLowerInvariant())
            .Order(StringComparer.Ordinal)
            .SequenceEqual(
                right.Select(candidate => SelectionKey(candidate) + "\n" + candidate.SourceHash.ToLowerInvariant())
                    .Order(StringComparer.Ordinal),
                StringComparer.Ordinal);

    private static string SelectionKey(TrustedCandidateRecord candidate) =>
        candidate.OwnerId + "\n" + candidate.FamilyId;

    private static async Task WritePreflightAsync(
        TextWriter output,
        CandidatePreflightResult result,
        CancellationToken cancellationToken)
    {
        await output.WriteLineAsync(
            JsonSerializer.Serialize(result, JsonOptions).AsMemory(),
            cancellationToken);
        await output.FlushAsync(cancellationToken);
    }

    private sealed record CandidatePreflightResult(
        bool Succeeded,
        int ProcessId,
        string OwnerId,
        string FamilyId,
        string SourceHash,
        string[] ActiveSourceHashes,
        string? Error);

    private sealed record SupervisorDelegation(CandidatePointerHead ExpectedHead);
}
