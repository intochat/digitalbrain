using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DigitalBrain.Poc.Runtime;

namespace DigitalBrain.Poc.ControlPlane;

public sealed class TrustedCandidateCatalogStore : ICandidateCatalogAuthority
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };
    private readonly PocDataRoot _root;
    private readonly AttestationSigner _signer;
    private readonly OwnerApprovalSigner? _approvalSigner;
    private readonly PointerSigner? _pointerSigner;
    private readonly Func<CancellationToken, Task>? _beforeAttestationCommit;
    private readonly Func<CancellationToken, Task>? _beforePointerHeadCommit;
    private readonly Func<CancellationToken, Task>? _afterPointerLedgerRead;

    public TrustedCandidateCatalogStore(PocDataRoot root, AttestationSigner signer)
        : this(root, signer, null)
    {
    }

    internal TrustedCandidateCatalogStore(
        PocDataRoot root,
        AttestationSigner signer,
        Func<CancellationToken, Task>? beforeAttestationCommit)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _signer = signer ?? throw new ArgumentNullException(nameof(signer));
        _beforeAttestationCommit = beforeAttestationCommit;
    }

    public TrustedCandidateCatalogStore(
        PocDataRoot root,
        AttestationSigner signer,
        OwnerApprovalSigner approvalSigner,
        PointerSigner pointerSigner)
        : this(root, signer, approvalSigner, pointerSigner, null)
    {
    }

    internal TrustedCandidateCatalogStore(
        PocDataRoot root,
        AttestationSigner signer,
        OwnerApprovalSigner approvalSigner,
        PointerSigner pointerSigner,
        Func<CancellationToken, Task>? beforePointerHeadCommit,
        Func<CancellationToken, Task>? afterPointerLedgerRead = null)
        : this(root, signer, null)
    {
        _approvalSigner = approvalSigner ?? throw new ArgumentNullException(nameof(approvalSigner));
        _pointerSigner = pointerSigner ?? throw new ArgumentNullException(nameof(pointerSigner));
        _beforePointerHeadCommit = beforePointerHeadCommit;
        _afterPointerLedgerRead = afterPointerLedgerRead;
    }

    public async Task WriteAttestationAsync(
        CandidateAttestation attestation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attestation);
        if (!_signer.Verify(attestation) ||
            attestation.Payload is null ||
            !string.Equals(attestation.Payload.RunId, _root.RunId, StringComparison.Ordinal))
        {
            throw new CryptographicException("The control plane accepts only its trusted run authority.");
        }

        var directory = Path.Combine(_root.ControlPlaneRoot, "attestations");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, FileName(attestation.Payload.CandidateId));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(attestation, JsonOptions);
        await WriteNewAtomicAsync(path, bytes, cancellationToken, _beforeAttestationCommit);
    }

    public async Task<CandidateAttestation> ReadAttestationAsync(
        string candidateId,
        CancellationToken cancellationToken = default)
    {
        var bytes = await File.ReadAllBytesAsync(AttestationPath(candidateId), cancellationToken);
        return JsonSerializer.Deserialize<CandidateAttestation>(bytes, JsonOptions) ??
            throw new InvalidDataException("The trusted candidate attestation is empty.");
    }

    public async Task WriteDiagnosticAsync(
        CandidateQuarantineDiagnostic diagnostic,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        var directory = Path.Combine(_root.ControlPlaneRoot, "diagnostics");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, FileName(diagnostic.CandidateId));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(diagnostic, JsonOptions);
        await WriteNewAtomicAsync(path, bytes, cancellationToken, null);
    }

    public async Task<CandidateQuarantineDiagnostic> ReadDiagnosticAsync(
        string candidateId,
        CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(_root.ControlPlaneRoot, "diagnostics", FileName(candidateId));
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        var diagnostic = JsonSerializer.Deserialize<CandidateQuarantineDiagnostic>(bytes, JsonOptions) ??
            throw new InvalidDataException("The quarantine diagnostic is empty.");
        return diagnostic with { Path = path };
    }

    public Task<bool> ExistsAsync(
        string candidateId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(
            IsCanonicalCandidateId(candidateId) && File.Exists(AttestationPath(candidateId)));
    }

    public async Task<AttestationVerificationResult> VerifyForBootAsync(
        string candidateId,
        CancellationToken cancellationToken = default)
    {
        if (!IsCanonicalCandidateId(candidateId))
        {
            return Failed(AttestationFailure.MalformedAttestation);
        }

        CandidateAttestation? attestation;
        try
        {
            var bytes = await File.ReadAllBytesAsync(AttestationPath(candidateId), cancellationToken);
            attestation = JsonSerializer.Deserialize<CandidateAttestation>(bytes, JsonOptions);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (FileNotFoundException)
        {
            return Failed(AttestationFailure.Missing);
        }
        catch (DirectoryNotFoundException)
        {
            return Failed(AttestationFailure.Missing);
        }
        catch (JsonException)
        {
            return Failed(AttestationFailure.MalformedAttestation);
        }
        catch (IOException)
        {
            return Failed(AttestationFailure.AttestationUnreadable);
        }
        catch (UnauthorizedAccessException)
        {
            return Failed(AttestationFailure.AttestationUnreadable);
        }
        catch (Exception)
        {
            return Failed(AttestationFailure.AttestationUnreadable);
        }

        if (attestation is null ||
            attestation.Payload is null ||
            !AttestationSigner.IsStructurallyValid(attestation.Payload))
        {
            return Failed(AttestationFailure.MalformedAttestation);
        }

        if (!_signer.Verify(attestation) ||
            !string.Equals(attestation.Payload.CandidateId, candidateId, StringComparison.Ordinal) ||
            !string.Equals(attestation.Payload.RunId, _root.RunId, StringComparison.Ordinal))
        {
            return Failed(AttestationFailure.Signature);
        }

        var candidateDirectory = Path.Combine(_root.CandidateRoot, candidateId);
        if (!HasNoUnexpectedCandidateContents(candidateDirectory))
        {
            return Failed(AttestationFailure.CandidateInventory);
        }

        try
        {
            var evidenceHash = Hash(await File.ReadAllBytesAsync(
                Path.Combine(candidateDirectory, "candidate.json"),
                cancellationToken));
            if (!string.Equals(
                    evidenceHash,
                    attestation.Payload.CandidateMetadataHash,
                    StringComparison.Ordinal))
            {
                return Failed(AttestationFailure.CandidateMetadataHash);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Failed(AttestationFailure.CandidateMetadataUnavailable);
        }

        try
        {
            var sourceHash = Hash(await File.ReadAllBytesAsync(
                Path.Combine(candidateDirectory, "elon-chart.cs"),
                cancellationToken));
            if (!string.Equals(sourceHash, attestation.Payload.SourceHash, StringComparison.Ordinal))
            {
                return Failed(AttestationFailure.SourceHash);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Failed(AttestationFailure.SourceUnavailable);
        }

        try
        {
            var assemblyHash = Hash(await File.ReadAllBytesAsync(
                Path.Combine(candidateDirectory, "module.dll"),
                cancellationToken));
            return string.Equals(assemblyHash, attestation.Payload.AssemblyHash, StringComparison.Ordinal)
                ? new AttestationVerificationResult(true, AttestationFailure.None)
                : Failed(AttestationFailure.AssemblyHash);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Failed(AttestationFailure.AssemblyUnavailable);
        }
    }

    public async Task<TrustedCandidateRecord> ReadTrustedCandidateAsync(
        string candidateId,
        CancellationToken cancellationToken = default)
    {
        var attestation = await ReadAttestationAsync(candidateId, cancellationToken);
        if (!_signer.Verify(attestation) ||
            attestation.Payload is null ||
            !string.Equals(attestation.Payload.CandidateId, candidateId, StringComparison.Ordinal) ||
            !string.Equals(attestation.Payload.RunId, _root.RunId, StringComparison.Ordinal))
        {
            throw new CryptographicException("The trusted candidate record has an invalid authority signature.");
        }

        var payload = attestation.Payload;
        return new TrustedCandidateRecord(
            payload.CandidateId,
            payload.RunId,
            payload.OwnerId,
            payload.FamilyId,
            payload.Revision,
            payload.Status,
            payload.SourcePath,
            payload.AssemblyPath,
            payload.SourceHash,
            payload.AssemblyHash,
            payload.CandidateMetadataHash,
            payload.GrantedInputAliases,
            payload.GrantedCandidateOutputAliases,
            payload.GrantedTrustedOutputAliases,
            payload.GrantedTargetScopes,
            payload.StateSchemaHash,
            payload.ResolvedReferences);
    }

    public Task<string?> ActiveAsync(
        string ownerId,
        string familyId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(familyId);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<string?>(null);
    }

    public async Task<CandidateCatalogRecord?> FindCandidateAsync(
        string candidateId,
        CancellationToken cancellationToken = default)
    {
        if (!IsCanonicalCandidateId(candidateId))
        {
            return null;
        }

        CandidateAttestation? attestation;
        try
        {
            var bytes = await File.ReadAllBytesAsync(AttestationPath(candidateId), cancellationToken);
            attestation = JsonSerializer.Deserialize<CandidateAttestation>(bytes, JsonOptions);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }

        if (attestation?.Payload is null ||
            !_signer.Verify(attestation) ||
            !string.Equals(attestation.Payload.CandidateId, candidateId, StringComparison.Ordinal) ||
            !string.Equals(attestation.Payload.RunId, _root.RunId, StringComparison.Ordinal))
        {
            return null;
        }

        var payload = attestation.Payload;
        return new CandidateCatalogRecord(
            payload.CandidateId,
            payload.RunId,
            payload.OwnerId,
            CandidateFamilyId.Parse(payload.FamilyId),
            payload.Revision,
            payload.SourceHash,
            payload.AssemblyHash,
            payload.CandidateMetadataHash,
            payload.StateSchemaHash);
    }

    public async Task IssueApprovalAsync(
        AuthenticatedPrincipal principal,
        CandidateCatalogRecord candidate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(candidate);
        var approvalSigner = RequireApprovalSigner();
        var attestation = await ReadAttestationAsync(candidate.CandidateId, cancellationToken);
        if (!_signer.Verify(attestation) ||
            attestation.Payload is null ||
            !string.Equals(attestation.Payload.OwnerId, principal.OwnerId, StringComparison.Ordinal) ||
            !Matches(candidate, attestation.Payload))
        {
            throw new AuthorizationException(
                "The authenticated owner is not bound to the exact signed candidate attestation.");
        }

        var approval = approvalSigner.Sign(new OwnerApprovalPayload(
            candidate.CandidateId,
            candidate.RunId,
            candidate.OwnerId,
            candidate.Family.Value,
            candidate.SourceHash,
            candidate.AssemblyHash,
            candidate.CandidateMetadataHash,
            Hash(AttestationSigner.CanonicalPayload(attestation.Payload))));
        var directory = Path.Combine(_root.ControlPlaneRoot, "approvals");
        Directory.CreateDirectory(directory);
        var path = ApprovalPath(candidate.CandidateId);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(approval, JsonOptions);
        try
        {
            await WriteNewAtomicAsync(path, bytes, cancellationToken, null);
        }
        catch (IOException) when (File.Exists(path))
        {
            var existing = await ReadApprovalAsync(candidate.CandidateId, cancellationToken);
            if (!approvalSigner.Verify(existing) || existing.Payload != approval.Payload)
            {
                throw new CryptographicException("A conflicting owner approval already exists.");
            }
        }
    }

    public async Task<OwnerApproval> ReadApprovalAsync(
        string candidateId,
        CancellationToken cancellationToken = default)
    {
        var bytes = await File.ReadAllBytesAsync(ApprovalPath(candidateId), cancellationToken);
        return JsonSerializer.Deserialize<OwnerApproval>(bytes, JsonOptions) ??
            throw new InvalidDataException("The signed owner approval is empty.");
    }

    public async Task<bool> ApprovalExistsAsync(
        string candidateId,
        CancellationToken cancellationToken = default)
    {
        if (!IsCanonicalCandidateId(candidateId) || !File.Exists(ApprovalPath(candidateId)))
        {
            return false;
        }

        try
        {
            var approval = await ReadApprovalAsync(candidateId, cancellationToken);
            var candidate = await FindCandidateAsync(candidateId, cancellationToken);
            var attestation = await ReadAttestationAsync(candidateId, cancellationToken);
            return candidate is not null &&
                attestation.Payload is not null &&
                _signer.Verify(attestation) &&
                RequireApprovalSigner().Verify(approval) &&
                ApprovalMatches(approval.Payload, candidate) &&
                string.Equals(
                    approval.Payload.AttestationPayloadHash,
                    Hash(AttestationSigner.CanonicalPayload(attestation.Payload)),
                    StringComparison.Ordinal);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task<TrustedCandidateRecord?> ReadVerifiedCandidateForActivationAsync(
        string candidateId,
        CancellationToken cancellationToken = default)
    {
        var verification = await VerifyForBootAsync(candidateId, cancellationToken);
        if (!verification.Succeeded || !await ApprovalExistsAsync(candidateId, cancellationToken))
        {
            return null;
        }

        return await ReadTrustedCandidateAsync(candidateId, cancellationToken);
    }

    public async Task<IReadOnlyList<TrustedCandidateRecord>> ReadAllVerifiedActiveCandidatesAsync(
        CancellationToken cancellationToken = default)
    {
        await using var ledgerLock = await AcquirePointerLedgerLockAsync(cancellationToken);
        var pointerRoot = Path.Combine(_root.ControlPlaneRoot, "pointers");
        var ledger = await ReadPointerLedgerUnlockedAsync(cancellationToken);
        if (!Directory.Exists(pointerRoot) &&
            ledger.Directories.Count == 0 &&
            !ledger.HasAnchors)
        {
            return [];
        }

        var records = new List<TrustedCandidateRecord>();
        var discoveredDirectories = Directory.Exists(pointerRoot)
            ? Directory.EnumerateFiles(
                    pointerRoot,
                    "current.json",
                    SearchOption.AllDirectories)
                .Select(path => Path.GetDirectoryName(path)!)
                .Concat(Directory.EnumerateFiles(
                    pointerRoot,
                    "head.json",
                    SearchOption.AllDirectories)
                    .Select(path => Path.GetDirectoryName(path)!))
                .Concat(Directory.EnumerateDirectories(
                    pointerRoot,
                    "history",
                    SearchOption.AllDirectories)
                    .Select(path => Directory.GetParent(path)!.FullName))
            : [];
        foreach (var directory in ledger.Directories)
        {
            if (!Directory.Exists(directory))
            {
                throw new CryptographicException(
                    "A signed pointer ledger references a deleted active family directory.");
            }
        }

        foreach (var directory in ledger.AnchorDirectories)
        {
            if (!Directory.Exists(directory))
            {
                throw new CryptographicException(
                    "A trusted pointer ledger anchor references a deleted active family directory.");
            }
        }

        var directories = discoveredDirectories
            .Concat(ledger.Directories)
            .Concat(ledger.AnchorDirectories)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (var directory in directories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = await ReadPointerSnapshotFromDirectoryAsync(directory, cancellationToken);
            if (snapshot is null)
            {
                if (ledger.ContainsDirectory(directory) || ledger.HasAnchorFor(directory))
                {
                    throw new CryptographicException(
                        "A trusted pointer publication references an unpublished active family.");
                }

                continue;
            }

            ValidateLedgerPublication(directory, snapshot, ledger);

            if (!snapshot.Verification.Succeeded)
            {
                if (snapshot.Verification.Failure == PointerVerificationFailure.Missing &&
                    snapshot.Head.Version > 0 &&
                    IsZeroHash(snapshot.Head.CurrentCandidateSourceHash))
                {
                    continue;
                }

                throw new CryptographicException(
                    $"An active pointer failed closed: {snapshot.Verification.Failure}.");
            }

            var record = await ReadVerifiedCandidateForActivationAsync(
                snapshot.Verification.Pointer!.CandidateSourceHash,
                cancellationToken) ?? throw new CryptographicException(
                    "An active pointer does not reference complete signed candidate evidence and approval.");
            if (!string.Equals(
                    record.OwnerId,
                    snapshot.Verification.Pointer.OwnerId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    record.FamilyId,
                    snapshot.Verification.Pointer.FamilyId,
                    StringComparison.Ordinal))
            {
                throw new CryptographicException("An active pointer crosses its signed owner or family boundary.");
            }

            records.Add(record);
        }

        return records
            .OrderBy(record => record.OwnerId, StringComparer.Ordinal)
            .ThenBy(record => record.FamilyId, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<CandidateCatalogRecord?> ActiveCandidateAsync(
        AuthenticatedPrincipal principal,
        CandidateFamilyId family,
        CancellationToken cancellationToken = default)
    {
        var verified = await VerifyActivePointerAsync(principal, family, cancellationToken);
        return verified.Succeeded
            ? await FindCandidateBySourceHashAsync(
                principal.OwnerId,
                family,
                verified.Pointer!.CandidateSourceHash,
                cancellationToken)
            : null;
    }

    public async Task<CandidateCatalogRecord?> PreviousCandidateAsync(
        AuthenticatedPrincipal principal,
        CandidateFamilyId family,
        CancellationToken cancellationToken = default)
    {
        var verified = await VerifyActivePointerAsync(principal, family, cancellationToken);
        if (!verified.Succeeded || IsZeroHash(verified.Pointer!.PreviousCandidateSourceHash))
        {
            return null;
        }

        return await FindCandidateBySourceHashAsync(
            principal.OwnerId,
            family,
            verified.Pointer.PreviousCandidateSourceHash,
            cancellationToken);
    }

    public async Task<bool> WasRolledBackAsync(
        AuthenticatedPrincipal principal,
        CandidateFamilyId family,
        string candidateSourceHash,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(principal);
        if (!IsCanonicalCandidateId(candidateSourceHash))
        {
            return false;
        }

        var current = await ReadVerifiedPointerSnapshotAsync(principal, family, cancellationToken);
        var pointer = current.Pointer;
        while (pointer is not null)
        {
            if (string.Equals(
                pointer.PreviousCandidateSourceHash,
                candidateSourceHash,
                StringComparison.Ordinal))
            {
                return true;
            }

            if (IsZeroHash(pointer.ParentPayloadHash))
            {
                return false;
            }

            if (pointer.Version <= 1)
            {
                throw new CryptographicException(
                    "A signed pointer has a non-root parent with no predecessor version.");
            }

            pointer = await ReadVerifiedPointerHistoryAsync(
                principal,
                family,
                pointer.ParentPayloadHash,
                pointer.Version - 1,
                cancellationToken);
        }

        return false;
    }

    public async Task<CandidatePointerHead> ReadPointerHeadAsync(
        AuthenticatedPrincipal principal,
        CandidateFamilyId family,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await ReadVerifiedPointerSnapshotAsync(
            principal,
            family,
            cancellationToken);
        return snapshot.Head;
    }

    public async Task<VerifiedPointerSnapshot> ReadVerifiedPointerSnapshotAsync(
        AuthenticatedPrincipal principal,
        CandidateFamilyId family,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await ReadPointerSnapshotAsync(principal, family, cancellationToken);
        if (snapshot.Verification.Succeeded)
        {
            return new VerifiedPointerSnapshot(snapshot.Head, snapshot.Verification.Pointer);
        }

        if (snapshot.Verification.Failure == PointerVerificationFailure.Missing)
        {
            return new VerifiedPointerSnapshot(snapshot.Head, null);
        }

        throw new CryptographicException(
            $"The candidate pointer snapshot failed closed: {snapshot.Verification.Failure}.");
    }

    public async Task<ActiveCandidatePointer> ReadPointerAsync(
        AuthenticatedPrincipal principal,
        CandidateFamilyId family,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await ReadVerifiedPointerSnapshotAsync(
            principal,
            family,
            cancellationToken);
        return snapshot.Pointer ?? throw new FileNotFoundException("No active candidate pointer exists.");
    }

    public async Task<PointerVerificationResult> VerifyActivePointerAsync(
        AuthenticatedPrincipal principal,
        CandidateFamilyId family,
        CancellationToken cancellationToken = default)
    {
        return (await ReadPointerSnapshotAsync(principal, family, cancellationToken)).Verification;
    }

    public async Task<PointerAdvanceResult> TryAdvancePointerHeadAsync(
        CandidatePointerHead expected,
        ActiveCandidatePointer proposed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(proposed);
        if (!RequirePointerSigner().Verify(proposed) ||
            !string.Equals(expected.OwnerId, proposed.OwnerId, StringComparison.Ordinal) ||
            !string.Equals(expected.FamilyId, proposed.FamilyId, StringComparison.Ordinal) ||
            expected.Version == long.MaxValue ||
            proposed.Version != expected.Version + 1 ||
            !string.Equals(proposed.ParentPayloadHash, expected.CurrentPayloadHash, StringComparison.Ordinal) ||
            !string.Equals(
                proposed.PreviousCandidateSourceHash,
                expected.CurrentCandidateSourceHash,
                StringComparison.Ordinal))
        {
            return new PointerAdvanceResult(false);
        }

        var directory = KeyDirectory(expected.OwnerId, expected.FamilyId);
        await using var ledgerLock = await AcquirePointerLedgerLockAsync(cancellationToken);
        Directory.CreateDirectory(directory);
        await using var pointerLock = await AcquireFileLockAsync(
            Path.Combine(directory, "head.lock"),
            cancellationToken);
        var principal = new AuthenticatedPrincipal(expected.OwnerId);
        var family = CandidateFamilyId.Parse(expected.FamilyId);
        var snapshot = await ReadPointerSnapshotUnlockedAsync(principal, family, cancellationToken);
        var ledger = await ReadPointerLedgerUnlockedAsync(cancellationToken);
        ValidateLedgerPublication(directory, snapshot, ledger);
        if ((!snapshot.Verification.Succeeded &&
                snapshot.Verification.Failure != PointerVerificationFailure.Missing) ||
            snapshot.Head != expected)
        {
            return new PointerAdvanceResult(false);
        }

        var pointerPath = CurrentPointerPath(expected.OwnerId, expected.FamilyId);
        var headPath = HeadPath(expected.OwnerId, expected.FamilyId);
        var historyPath = Path.Combine(directory, "history", proposed.PayloadHash + ".json");
        var anchorCurrentPath = PointerLedgerAnchorCurrentPath(
            expected.OwnerId,
            expected.FamilyId);
        var oldPointer = File.Exists(pointerPath)
            ? await File.ReadAllBytesAsync(pointerPath, cancellationToken)
            : null;
        var oldHead = File.Exists(headPath)
            ? await File.ReadAllBytesAsync(headPath, cancellationToken)
            : null;
        var oldAnchorCurrent = File.Exists(anchorCurrentPath)
            ? await File.ReadAllBytesAsync(anchorCurrentPath, cancellationToken)
            : null;
        var pointerBytes = JsonSerializer.SerializeToUtf8Bytes(proposed, JsonOptions);
        var headBytes = JsonSerializer.SerializeToUtf8Bytes(CandidatePointerHead.From(proposed), JsonOptions);
        var ledgerCreated = false;
        var anchorHistoryCreated = false;
        try
        {
            ledgerCreated = await WriteImmutablePointerLedgerRecordAsync(
                proposed.PayloadHash,
                pointerBytes,
                cancellationToken);
            Directory.CreateDirectory(Path.GetDirectoryName(historyPath)!);
            try
            {
                await WriteNewAtomicAsync(
                    historyPath,
                    pointerBytes,
                    cancellationToken,
                    null);
            }
            catch (IOException) when (File.Exists(historyPath))
            {
                var existing = await File.ReadAllBytesAsync(historyPath, cancellationToken);
                if (!existing.AsSpan().SequenceEqual(pointerBytes))
                {
                    throw new CryptographicException(
                        "An immutable pointer history record conflicts with its payload hash.");
                }
            }

            await WriteReplaceAtomicAsync(pointerPath, pointerBytes, cancellationToken);
            if (_beforePointerHeadCommit is not null)
            {
                await _beforePointerHeadCommit(cancellationToken);
            }

            await WriteReplaceAtomicAsync(headPath, headBytes, cancellationToken);
            anchorHistoryCreated = await WriteImmutablePointerLedgerAnchorHistoryAsync(
                expected.OwnerId,
                expected.FamilyId,
                proposed.PayloadHash,
                pointerBytes,
                cancellationToken);
            await WriteReplaceAtomicAsync(anchorCurrentPath, pointerBytes, cancellationToken);
        }
        catch
        {
            await RestorePointerFileAsync(pointerPath, oldPointer);
            await RestorePointerFileAsync(headPath, oldHead);
            await RestorePointerFileAsync(anchorCurrentPath, oldAnchorCurrent);
            if (anchorHistoryCreated)
            {
                await RemoveUnpublishedPointerLedgerAnchorHistoryAsync(
                    expected.OwnerId,
                    expected.FamilyId,
                    proposed.PayloadHash,
                    pointerBytes);
            }

            if (ledgerCreated)
            {
                await RemoveUnpublishedPointerLedgerRecordAsync(proposed.PayloadHash, pointerBytes);
            }

            throw;
        }

        return new PointerAdvanceResult(true);
    }

    public async Task<bool> TryRestoreCanonicalEmptyPointerHeadAsync(
        ActiveCandidatePointer expected,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expected);
        if (!RequirePointerSigner().Verify(expected) ||
            expected.Version != 1 ||
            !IsZeroHash(expected.ParentPayloadHash) ||
            !IsZeroHash(expected.PreviousCandidateSourceHash))
        {
            return false;
        }

        var recovery = RequirePointerSigner().Sign(ActiveCandidatePointer.EmptyRecovery(
            CandidatePointerHead.From(expected)));
        return (await TryAdvancePointerHeadAsync(
            CandidatePointerHead.From(expected),
            recovery,
            cancellationToken)).Succeeded;
    }

    public Task ReplacePointerFileForTestAsync(
        AuthenticatedPrincipal principal,
        CandidateFamilyId family,
        ActiveCandidatePointer pointer,
        CancellationToken cancellationToken = default) =>
        WriteReplaceAtomicAsync(
            CurrentPointerPath(principal.OwnerId, family.Value),
            JsonSerializer.SerializeToUtf8Bytes(pointer, JsonOptions),
            cancellationToken);

    public Task ReplacePointerHeadFileForTestAsync(
        AuthenticatedPrincipal principal,
        CandidateFamilyId family,
        CandidatePointerHead head,
        CancellationToken cancellationToken = default) =>
        WriteReplaceAtomicAsync(
            HeadPath(principal.OwnerId, family.Value),
            JsonSerializer.SerializeToUtf8Bytes(head, JsonOptions),
            cancellationToken);

    private async Task<PointerSnapshotRead> ReadPointerSnapshotAsync(
        AuthenticatedPrincipal principal,
        CandidateFamilyId family,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(principal);
        var directory = KeyDirectory(principal.OwnerId, family.Value);
        await using var ledgerLock = await AcquirePointerLedgerLockAsync(cancellationToken);
        var ledger = await ReadPointerLedgerUnlockedAsync(cancellationToken);
        if (_afterPointerLedgerRead is not null)
        {
            await _afterPointerLedgerRead(cancellationToken);
        }

        if ((ledger.ContainsDirectory(directory) || ledger.HasAnchorFor(directory)) &&
            !Directory.Exists(directory))
        {
            throw new CryptographicException(
                "A signed pointer ledger references a deleted active family directory.");
        }

        Directory.CreateDirectory(directory);
        await using var pointerLock = await AcquireFileLockAsync(
            Path.Combine(directory, "head.lock"),
            cancellationToken);
        var snapshot = await ReadPointerSnapshotUnlockedAsync(principal, family, cancellationToken);
        ValidateLedgerPublication(directory, snapshot, ledger);
        return snapshot;
    }

    private async Task<PointerSnapshotRead?> ReadPointerSnapshotFromDirectoryAsync(
        string directory,
        CancellationToken cancellationToken)
    {
        await using var pointerLock = await AcquireFileLockAsync(
            Path.Combine(directory, "head.lock"),
            cancellationToken);
        var currentPointerPath = Path.Combine(directory, "current.json");
        var headPath = Path.Combine(directory, "head.json");
        ActiveCandidatePointer? pointer = null;
        CandidatePointerHead? head = null;
        try
        {
            if (File.Exists(currentPointerPath))
            {
                pointer = JsonSerializer.Deserialize<ActiveCandidatePointer>(
                    await File.ReadAllBytesAsync(currentPointerPath, cancellationToken),
                    JsonOptions) ?? throw new InvalidDataException("An active pointer is empty.");
            }

            if (File.Exists(headPath))
            {
                head = JsonSerializer.Deserialize<CandidatePointerHead>(
                    await File.ReadAllBytesAsync(headPath, cancellationToken),
                    JsonOptions) ?? throw new InvalidDataException("An active pointer head is empty.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new InvalidDataException("An active pointer publication is unreadable.", exception);
        }

        if (pointer is null && head is null)
        {
            var historyDirectory = Path.Combine(directory, "history");
            if (Directory.Exists(historyDirectory) &&
                Directory.EnumerateFiles(historyDirectory, "*.json", SearchOption.TopDirectoryOnly).Any())
            {
                throw new CryptographicException(
                    "An established active pointer publication was deleted.");
            }

            return null;
        }

        var ownerId = pointer?.OwnerId ?? head!.OwnerId;
        var familyId = pointer?.FamilyId ?? head!.FamilyId;
        CandidateFamilyId family;
        try
        {
            family = CandidateFamilyId.Parse(familyId);
        }
        catch (Exception exception)
        {
            throw new InvalidDataException("An active pointer publication has a malformed family.", exception);
        }

        var directoryFamily = Path.GetFileName(directory);
        var directoryOwnerHash = Path.GetFileName(Path.GetDirectoryName(directory));
        var expectedOwnerHash = Hash(System.Text.Encoding.UTF8.GetBytes(ownerId));
        if (!string.Equals(directoryFamily, family.Value, StringComparison.Ordinal) ||
            !string.Equals(directoryOwnerHash, expectedOwnerHash, StringComparison.Ordinal))
        {
            throw new CryptographicException("An active pointer is stored under another owner or family key.");
        }

        return await ReadPointerSnapshotUnlockedAsync(
            new AuthenticatedPrincipal(ownerId),
            family,
            cancellationToken);
    }

    private async Task<bool> WriteImmutablePointerLedgerRecordAsync(
        string payloadHash,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        var path = PointerLedgerPath(payloadHash);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            await WriteNewAtomicAsync(path, bytes, cancellationToken, null);
            return true;
        }
        catch (IOException) when (File.Exists(path))
        {
            var existing = await File.ReadAllBytesAsync(path, cancellationToken);
            if (!existing.AsSpan().SequenceEqual(bytes))
            {
                throw new CryptographicException(
                    "An immutable signed pointer ledger record conflicts with its payload hash.");
            }

            return false;
        }
    }

    private async Task RemoveUnpublishedPointerLedgerRecordAsync(string payloadHash, byte[] bytes)
    {
        var path = PointerLedgerPath(payloadHash);
        try
        {
            if (!File.Exists(path) ||
                !(await File.ReadAllBytesAsync(path)).AsSpan().SequenceEqual(bytes))
            {
                return;
            }

            File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
            File.Delete(path);
        }
        catch
        {
        }
    }

    private async Task<bool> WriteImmutablePointerLedgerAnchorHistoryAsync(
        string ownerId,
        string familyId,
        string payloadHash,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        var path = PointerLedgerAnchorHistoryPath(ownerId, familyId, payloadHash);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            await WriteNewAtomicAsync(path, bytes, cancellationToken, null);
            return true;
        }
        catch (IOException) when (File.Exists(path))
        {
            var existing = await File.ReadAllBytesAsync(path, cancellationToken);
            if (!existing.AsSpan().SequenceEqual(bytes))
            {
                throw new CryptographicException(
                    "An immutable trusted pointer ledger anchor conflicts with its payload hash.");
            }

            return false;
        }
    }

    private async Task RemoveUnpublishedPointerLedgerAnchorHistoryAsync(
        string ownerId,
        string familyId,
        string payloadHash,
        byte[] bytes)
    {
        var path = PointerLedgerAnchorHistoryPath(ownerId, familyId, payloadHash);
        try
        {
            if (!File.Exists(path) ||
                !(await File.ReadAllBytesAsync(path)).AsSpan().SequenceEqual(bytes))
            {
                return;
            }

            File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
            File.Delete(path);
        }
        catch
        {
        }
    }

    private static void ValidateLedgerPublication(
        string directory,
        PointerSnapshotRead snapshot,
        PointerLedger ledger)
    {
        if (!snapshot.Verification.Succeeded &&
            snapshot.Verification.Failure != PointerVerificationFailure.Missing)
        {
            return;
        }

        if (snapshot.Head.Version == 0)
        {
            if (ledger.ContainsDirectory(directory) || ledger.HasAnchorFor(directory))
            {
                throw new CryptographicException(
                    "An established pointer family was reset to an unsigned empty lineage.");
            }

            return;
        }

        if (!ledger.ContainsDirectory(directory))
        {
            throw new CryptographicException(
                "An active pointer head has no immutable signed ledger lineage.");
        }

        if (!ledger.ContainsPayload(snapshot.Head.CurrentPayloadHash))
        {
            throw new CryptographicException(
                "An active pointer head is not backed by its immutable signed ledger record.");
        }

        var latest = ledger.LatestFor(directory);
        if (latest is null ||
            CandidatePointerHead.From(latest) != snapshot.Head)
        {
            throw new CryptographicException(
                "An active pointer head replays a superseded signed ledger record.");
        }

        var anchorLatest = ledger.AnchorLatestFor(directory);
        if (anchorLatest is null)
        {
            throw new CryptographicException(
                "An active pointer head has no trusted monotonic ledger anchor.");
        }

        if (CandidatePointerHead.From(anchorLatest) != snapshot.Head)
        {
            throw new CryptographicException(
                "An active pointer head does not match its trusted monotonic ledger anchor.");
        }
    }

    private async Task<PointerLedger> ReadPointerLedgerUnlockedAsync(
        CancellationToken cancellationToken)
    {
        var ledger = Path.Combine(_root.RootPath, "pointer-ledger");
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var payloadHashes = new HashSet<string>(StringComparer.Ordinal);
        var recordsByDirectory = new Dictionary<string, List<ActiveCandidatePointer>>(
            StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(ledger))
        {
            foreach (var path in Directory.EnumerateFiles(ledger, "*.json", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var pointer = await ReadSignedPointerRecordAsync(
                    path,
                    "A signed pointer ledger record",
                    cancellationToken);
                if (!string.Equals(
                        Path.GetFileNameWithoutExtension(path),
                        pointer.PayloadHash,
                        StringComparison.Ordinal))
                {
                    throw new CryptographicException(
                        "A signed pointer ledger record is not named by its authenticated payload hash.");
                }

                var family = ParsePointerFamily(pointer, "A signed pointer ledger family");
                var directory = KeyDirectory(pointer.OwnerId, family.Value);
                if (!recordsByDirectory.TryGetValue(directory, out var records))
                {
                    records = [];
                    recordsByDirectory.Add(directory, records);
                }

                records.Add(pointer);
                directories.Add(directory);
                payloadHashes.Add(pointer.PayloadHash);
            }
        }

        var latest = recordsByDirectory.ToDictionary(
            pair => pair.Key,
            pair => ValidateContiguousPointerLineage(
                pair.Value,
                "The signed pointer ledger"),
            StringComparer.OrdinalIgnoreCase);
        var anchors = await ReadPointerLedgerAnchorsUnlockedAsync(cancellationToken);
        return new PointerLedger(directories, payloadHashes, latest, anchors);
    }

    private async Task<IReadOnlyDictionary<string, ActiveCandidatePointer>>
        ReadPointerLedgerAnchorsUnlockedAsync(CancellationToken cancellationToken)
    {
        var root = PointerLedgerAnchorRootPath();
        if (!Directory.Exists(root))
        {
            return new Dictionary<string, ActiveCandidatePointer>(StringComparer.OrdinalIgnoreCase);
        }

        var anchorDirectories = Directory.EnumerateFiles(
                root,
                "current.json",
                SearchOption.AllDirectories)
            .Select(path => Path.GetDirectoryName(path)!)
            .Concat(Directory.EnumerateDirectories(root, "history", SearchOption.AllDirectories)
                .Select(path => Directory.GetParent(path)!.FullName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var anchors = new Dictionary<string, ActiveCandidatePointer>(StringComparer.OrdinalIgnoreCase);
        foreach (var anchorDirectory in anchorDirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var currentPath = Path.Combine(anchorDirectory, "current.json");
            var historyDirectory = Path.Combine(anchorDirectory, "history");
            if (!File.Exists(currentPath) || !Directory.Exists(historyDirectory))
            {
                throw new CryptographicException("A trusted pointer ledger anchor is incomplete.");
            }

            var current = await ReadSignedPointerRecordAsync(
                currentPath,
                "A trusted pointer ledger anchor",
                cancellationToken);
            var family = ParsePointerFamily(current, "A trusted pointer ledger anchor family");
            if (!string.Equals(
                    Path.GetFullPath(anchorDirectory),
                    Path.GetFullPath(PointerLedgerAnchorDirectory(current.OwnerId, family.Value)),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new CryptographicException(
                    "A trusted pointer ledger anchor is stored under another owner or family key.");
            }

            var history = new List<ActiveCandidatePointer>();
            foreach (var path in Directory.EnumerateFiles(
                         historyDirectory,
                         "*.json",
                         SearchOption.TopDirectoryOnly))
            {
                var pointer = await ReadSignedPointerRecordAsync(
                    path,
                    "A trusted pointer ledger anchor history record",
                    cancellationToken);
                if (!string.Equals(
                        Path.GetFileNameWithoutExtension(path),
                        pointer.PayloadHash,
                        StringComparison.Ordinal) ||
                    !string.Equals(pointer.OwnerId, current.OwnerId, StringComparison.Ordinal) ||
                    !string.Equals(pointer.FamilyId, family.Value, StringComparison.Ordinal))
                {
                    throw new CryptographicException(
                        "A trusted pointer ledger anchor history record is malformed.");
                }

                history.Add(pointer);
            }

            var latest = ValidateContiguousPointerLineage(
                history,
                "The trusted pointer ledger anchor history");
            if (current != latest)
            {
                throw new CryptographicException(
                    "A trusted pointer ledger anchor is rolled back or does not match its history.");
            }

            var directory = KeyDirectory(current.OwnerId, family.Value);
            if (!anchors.TryAdd(directory, latest))
            {
                throw new CryptographicException(
                    "A trusted pointer ledger anchor has duplicate owner or family publications.");
            }
        }

        return anchors;
    }

    private async Task<ActiveCandidatePointer> ReadSignedPointerRecordAsync(
        string path,
        string recordName,
        CancellationToken cancellationToken)
    {
        ActiveCandidatePointer? pointer;
        try
        {
            pointer = JsonSerializer.Deserialize<ActiveCandidatePointer>(
                await File.ReadAllBytesAsync(path, cancellationToken),
                JsonOptions);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new CryptographicException($"{recordName} is unreadable.", exception);
        }

        if (pointer is null || !RequirePointerSigner().Verify(pointer))
        {
            throw new CryptographicException($"{recordName} is unauthenticated.");
        }

        return pointer;
    }

    private static CandidateFamilyId ParsePointerFamily(
        ActiveCandidatePointer pointer,
        string recordName)
    {
        try
        {
            return CandidateFamilyId.Parse(pointer.FamilyId);
        }
        catch (Exception exception)
        {
            throw new CryptographicException($"{recordName} is malformed.", exception);
        }
    }

    private static ActiveCandidatePointer ValidateContiguousPointerLineage(
        IReadOnlyCollection<ActiveCandidatePointer> pointers,
        string lineageName)
    {
        if (pointers.Count == 0)
        {
            throw new CryptographicException($"{lineageName} is empty.");
        }

        var byPayloadHash = new Dictionary<string, ActiveCandidatePointer>(StringComparer.Ordinal);
        var byVersion = new Dictionary<long, ActiveCandidatePointer>();
        foreach (var pointer in pointers)
        {
            if (!byPayloadHash.TryAdd(pointer.PayloadHash, pointer) ||
                !byVersion.TryAdd(pointer.Version, pointer))
            {
                throw new CryptographicException($"{lineageName} forks a family version.");
            }
        }

        var latest = byVersion.Values.MaxBy(pointer => pointer.Version)!;
        var traversed = new HashSet<string>(StringComparer.Ordinal);
        var current = latest;
        while (true)
        {
            if (!traversed.Add(current.PayloadHash))
            {
                throw new CryptographicException($"{lineageName} contains a cycle.");
            }

            if (current.Version == 1)
            {
                if (!IsZeroHash(current.ParentPayloadHash) ||
                    !IsZeroHash(current.PreviousCandidateSourceHash))
                {
                    throw new CryptographicException($"{lineageName} has an invalid root record.");
                }

                break;
            }

            if (current.Version <= 1 ||
                !byPayloadHash.TryGetValue(current.ParentPayloadHash, out var previous) ||
                previous.Version != current.Version - 1 ||
                !string.Equals(
                    previous.CandidateSourceHash,
                    current.PreviousCandidateSourceHash,
                    StringComparison.Ordinal))
            {
                throw new CryptographicException($"{lineageName} is not a contiguous parent chain.");
            }

            current = previous;
        }

        if (traversed.Count != pointers.Count)
        {
            throw new CryptographicException($"{lineageName} contains an orphaned record.");
        }

        return latest;
    }

    private async Task<ActiveCandidatePointer> ReadVerifiedPointerHistoryAsync(
        AuthenticatedPrincipal principal,
        CandidateFamilyId family,
        string payloadHash,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        if (!IsLowerHash(payloadHash) || expectedVersion <= 0)
        {
            throw new CryptographicException("The signed pointer history chain is malformed.");
        }

        var path = Path.Combine(
            KeyDirectory(principal.OwnerId, family.Value),
            "history",
            payloadHash + ".json");
        ActiveCandidatePointer? pointer;
        try
        {
            pointer = JsonSerializer.Deserialize<ActiveCandidatePointer>(
                await File.ReadAllBytesAsync(path, cancellationToken),
                JsonOptions);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new CryptographicException("A required signed pointer history record is unavailable.", exception);
        }

        if (pointer is null ||
            !RequirePointerSigner().Verify(pointer) ||
            !string.Equals(pointer.OwnerId, principal.OwnerId, StringComparison.Ordinal) ||
            !string.Equals(pointer.FamilyId, family.Value, StringComparison.Ordinal) ||
            !string.Equals(pointer.PayloadHash, payloadHash, StringComparison.Ordinal) ||
            pointer.Version != expectedVersion)
        {
            throw new CryptographicException("A signed pointer history record is malformed or unauthenticated.");
        }

        return pointer;
    }

    private async Task<PointerSnapshotRead> ReadPointerSnapshotUnlockedAsync(
        AuthenticatedPrincipal principal,
        CandidateFamilyId family,
        CancellationToken cancellationToken)
    {
        var empty = CandidatePointerHead.Empty(principal.OwnerId, family.Value);
        ActiveCandidatePointer? pointer = null;
        CandidatePointerHead? head = null;
        var pointerPath = CurrentPointerPath(principal.OwnerId, family.Value);
        var headPath = HeadPath(principal.OwnerId, family.Value);

        try
        {
            if (File.Exists(pointerPath))
            {
                pointer = JsonSerializer.Deserialize<ActiveCandidatePointer>(
                    await File.ReadAllBytesAsync(pointerPath, cancellationToken),
                    JsonOptions) ?? throw new InvalidDataException("The active candidate pointer is empty.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new PointerSnapshotRead(
                empty,
                new PointerVerificationResult(false, PointerVerificationFailure.Malformed, null));
        }

        try
        {
            if (File.Exists(headPath))
            {
                head = JsonSerializer.Deserialize<CandidatePointerHead>(
                    await File.ReadAllBytesAsync(headPath, cancellationToken),
                    JsonOptions) ?? throw new InvalidDataException("The candidate pointer head is empty.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new PointerSnapshotRead(
                empty,
                new PointerVerificationResult(false, PointerVerificationFailure.Malformed, null));
        }

        if (pointer is null)
        {
            if ((head is null || head == empty) && HasPointerHistory(principal, family))
            {
                return new PointerSnapshotRead(
                    empty,
                    new PointerVerificationResult(false, PointerVerificationFailure.Malformed, null));
            }

            if (head is null || head == empty)
            {
                return new PointerSnapshotRead(
                    head ?? empty,
                    new PointerVerificationResult(false, PointerVerificationFailure.Missing, null));
            }

            return new PointerSnapshotRead(
                head,
                new PointerVerificationResult(false, PointerVerificationFailure.Malformed, null));
        }

        if (!RequirePointerSigner().Verify(pointer))
        {
            return new PointerSnapshotRead(
                head ?? empty,
                new PointerVerificationResult(false, PointerVerificationFailure.InvalidSignature, null));
        }

        if (head is null)
        {
            return new PointerSnapshotRead(
                empty,
                new PointerVerificationResult(false, PointerVerificationFailure.StaleOrReplayed, null));
        }

        if (!IsStructurallyValidHead(head, principal, family))
        {
            return new PointerSnapshotRead(
                head,
                new PointerVerificationResult(false, PointerVerificationFailure.Malformed, null));
        }

        if (!PointerMatchesHead(pointer, head, principal, family))
        {
            return new PointerSnapshotRead(
                head,
                new PointerVerificationResult(false, PointerVerificationFailure.StaleOrReplayed, null));
        }

        if (IsZeroHash(pointer.CandidateSourceHash))
        {
            return new PointerSnapshotRead(
                head,
                new PointerVerificationResult(false, PointerVerificationFailure.Missing, null));
        }

        return new PointerSnapshotRead(
            head,
            new PointerVerificationResult(true, PointerVerificationFailure.None, pointer));
    }

    private static bool IsStructurallyValidHead(
        CandidatePointerHead head,
        AuthenticatedPrincipal principal,
        CandidateFamilyId family)
    {
        if (!string.Equals(head.OwnerId, principal.OwnerId, StringComparison.Ordinal) ||
            !string.Equals(head.FamilyId, family.Value, StringComparison.Ordinal))
        {
            return false;
        }

        if (head.Version == 0)
        {
            return head == CandidatePointerHead.Empty(principal.OwnerId, family.Value);
        }

        return head.Version > 0 &&
            IsLowerHash(head.CurrentPayloadHash) &&
            IsLowerHash(head.ParentPayloadHash) &&
            IsLowerHash(head.CurrentCandidateSourceHash) &&
            IsLowerHash(head.PreviousCandidateSourceHash);
    }

    private static bool PointerMatchesHead(
        ActiveCandidatePointer pointer,
        CandidatePointerHead head,
        AuthenticatedPrincipal principal,
        CandidateFamilyId family) =>
        string.Equals(pointer.OwnerId, principal.OwnerId, StringComparison.Ordinal) &&
        string.Equals(pointer.FamilyId, family.Value, StringComparison.Ordinal) &&
        pointer.Version == head.Version &&
        string.Equals(pointer.PayloadHash, head.CurrentPayloadHash, StringComparison.Ordinal) &&
        string.Equals(pointer.ParentPayloadHash, head.ParentPayloadHash, StringComparison.Ordinal) &&
        string.Equals(pointer.CandidateSourceHash, head.CurrentCandidateSourceHash, StringComparison.Ordinal) &&
        string.Equals(
            pointer.PreviousCandidateSourceHash,
            head.PreviousCandidateSourceHash,
            StringComparison.Ordinal);

    private static bool IsLowerHash(string? value) =>
        value is { Length: 64 } && value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private bool HasPointerHistory(AuthenticatedPrincipal principal, CandidateFamilyId family)
    {
        var history = Path.Combine(KeyDirectory(principal.OwnerId, family.Value), "history");
        try
        {
            return Directory.Exists(history) &&
                Directory.EnumerateFiles(history, "*.json", SearchOption.TopDirectoryOnly).Any();
        }
        catch (Exception)
        {
            return true;
        }
    }

    private static async Task RestorePointerFileAsync(string path, byte[]? bytes)
    {
        if (bytes is null)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return;
        }

        await WriteReplaceAtomicAsync(path, bytes, CancellationToken.None);
    }

    private sealed record PointerSnapshotRead(
        CandidatePointerHead Head,
        PointerVerificationResult Verification);

    private sealed record PointerLedger(
        IReadOnlySet<string> Directories,
        IReadOnlySet<string> PayloadHashes,
        IReadOnlyDictionary<string, ActiveCandidatePointer> Latest,
        IReadOnlyDictionary<string, ActiveCandidatePointer> AnchorLatest)
    {
        public IEnumerable<string> AnchorDirectories => AnchorLatest.Keys;

        public bool HasAnchors => AnchorLatest.Count != 0;

        public bool ContainsDirectory(string directory) => Directories.Contains(directory);

        public bool ContainsPayload(string payloadHash) => PayloadHashes.Contains(payloadHash);

        public bool HasAnchorFor(string directory) => AnchorLatest.ContainsKey(directory);

        public ActiveCandidatePointer? LatestFor(string directory) =>
            Latest.TryGetValue(directory, out var pointer) ? pointer : null;

        public ActiveCandidatePointer? AnchorLatestFor(string directory) =>
            AnchorLatest.TryGetValue(directory, out var pointer) ? pointer : null;
    }

    private string AttestationPath(string candidateId) =>
        Path.Combine(_root.ControlPlaneRoot, "attestations", FileName(candidateId));

    private string ApprovalPath(string candidateId) =>
        Path.Combine(_root.ControlPlaneRoot, "approvals", FileName(candidateId));

    private string KeyDirectory(string ownerId, string familyId) =>
        Path.Combine(_root.ControlPlaneRoot, "pointers", Hash(System.Text.Encoding.UTF8.GetBytes(ownerId)), familyId);

    private string CurrentPointerPath(string ownerId, string familyId) =>
        Path.Combine(KeyDirectory(ownerId, familyId), "current.json");

    private string HeadPath(string ownerId, string familyId) =>
        Path.Combine(KeyDirectory(ownerId, familyId), "head.json");

    private string PointerLedgerPath(string payloadHash) =>
        Path.Combine(_root.RootPath, "pointer-ledger", payloadHash + ".json");

    private string PointerLedgerAnchorRootPath() =>
        _root.PointerLedgerAuthorityPath;

    private string PointerLedgerAnchorDirectory(string ownerId, string familyId) =>
        Path.Combine(
            PointerLedgerAnchorRootPath(),
            Hash(System.Text.Encoding.UTF8.GetBytes(ownerId)),
            familyId);

    private string PointerLedgerAnchorCurrentPath(string ownerId, string familyId) =>
        Path.Combine(PointerLedgerAnchorDirectory(ownerId, familyId), "current.json");

    private string PointerLedgerAnchorHistoryPath(
        string ownerId,
        string familyId,
        string payloadHash) =>
        Path.Combine(
            PointerLedgerAnchorDirectory(ownerId, familyId),
            "history",
            payloadHash + ".json");

    private static AttestationVerificationResult Failed(AttestationFailure failure) =>
        new(false, failure);

    private static bool IsCanonicalCandidateId(string? candidateId) =>
        candidateId is not null &&
        candidateId.Length == 64 &&
        candidateId.All(character => character is (>= '0' and <= '9') or (>= 'a' and <= 'f'));

    private static string FileName(string candidateId)
    {
        if (!IsCanonicalCandidateId(candidateId))
        {
            throw new FormatException("An attested candidate ID must be a lowercase SHA-256 digest.");
        }

        return candidateId + ".json";
    }

    private static string Hash(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private OwnerApprovalSigner RequireApprovalSigner() =>
        _approvalSigner ?? throw new InvalidOperationException("This trusted store has no owner-approval authority.");

    private PointerSigner RequirePointerSigner() =>
        _pointerSigner ?? throw new InvalidOperationException("This trusted store has no pointer authority.");

    private static bool Matches(CandidateCatalogRecord candidate, CandidateAttestationPayload payload) =>
        string.Equals(candidate.CandidateId, payload.CandidateId, StringComparison.Ordinal) &&
        string.Equals(candidate.RunId, payload.RunId, StringComparison.Ordinal) &&
        string.Equals(candidate.OwnerId, payload.OwnerId, StringComparison.Ordinal) &&
        string.Equals(candidate.Family.Value, payload.FamilyId, StringComparison.Ordinal) &&
        string.Equals(candidate.Revision, payload.Revision, StringComparison.Ordinal) &&
        string.Equals(candidate.SourceHash, payload.SourceHash, StringComparison.Ordinal) &&
        string.Equals(candidate.AssemblyHash, payload.AssemblyHash, StringComparison.Ordinal) &&
        string.Equals(candidate.CandidateMetadataHash, payload.CandidateMetadataHash, StringComparison.Ordinal) &&
        string.Equals(candidate.StateSchemaHash, payload.StateSchemaHash, StringComparison.Ordinal);

    private static bool ApprovalMatches(OwnerApprovalPayload approval, CandidateCatalogRecord candidate) =>
        string.Equals(approval.CandidateId, candidate.CandidateId, StringComparison.Ordinal) &&
        string.Equals(approval.RunId, candidate.RunId, StringComparison.Ordinal) &&
        string.Equals(approval.OwnerId, candidate.OwnerId, StringComparison.Ordinal) &&
        string.Equals(approval.FamilyId, candidate.Family.Value, StringComparison.Ordinal) &&
        string.Equals(approval.SourceHash, candidate.SourceHash, StringComparison.Ordinal) &&
        string.Equals(approval.AssemblyHash, candidate.AssemblyHash, StringComparison.Ordinal) &&
        string.Equals(
            approval.CandidateMetadataHash,
            candidate.CandidateMetadataHash,
            StringComparison.Ordinal);

    private async Task<CandidateCatalogRecord?> FindCandidateBySourceHashAsync(
        string ownerId,
        CandidateFamilyId family,
        string sourceHash,
        CancellationToken cancellationToken)
    {
        var candidate = await FindCandidateAsync(sourceHash, cancellationToken);
        return candidate is not null &&
            string.Equals(candidate.OwnerId, ownerId, StringComparison.Ordinal) &&
            candidate.Family == family
            ? candidate
            : null;
    }

    private static bool IsZeroHash(string value) =>
        string.Equals(value, new string('0', 64), StringComparison.Ordinal);

    private static bool HasNoUnexpectedCandidateContents(string directory)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                return true;
            }

            var files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(directory, path).Replace('\\', '/'))
                .ToArray();
            return !Directory.EnumerateDirectories(directory, "*", SearchOption.AllDirectories).Any() &&
                files.All(path => path is "candidate.json" or "elon-chart.cs" or "module.dll");
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static async Task WriteNewAtomicAsync(
        string path,
        byte[] bytes,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task>? beforeCommit)
    {
        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.SetAttributes(temporary, File.GetAttributes(temporary) | FileAttributes.ReadOnly);
            if (beforeCommit is not null)
            {
                await beforeCommit(cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, path);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.SetAttributes(temporary, File.GetAttributes(temporary) & ~FileAttributes.ReadOnly);
                File.Delete(temporary);
            }
        }
    }

    private static async Task WriteReplaceAtomicAsync(
        string path,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static async Task<FileStream> AcquireFileLockAsync(
        string path,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(
                    path,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    1,
                    FileOptions.WriteThrough);
            }
            catch (IOException)
            {
                await Task.Delay(10, cancellationToken);
            }
        }
    }

    private Task<FileStream> AcquirePointerLedgerLockAsync(CancellationToken cancellationToken) =>
        AcquireFileLockAsync(Path.Combine(_root.RootPath, "pointer-ledger.lock"), cancellationToken);
}
