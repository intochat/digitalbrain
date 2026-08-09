using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using DigitalBrain.Poc.ControlPlane;
using DigitalBrain.Poc.Runtime;

namespace DigitalBrain.Poc.Host;

public sealed class HostSupervisor : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly PocDataRoot _root;
    private readonly TrustedCandidateCatalogStore _controlPlane;
    private readonly PointerSigner _pointerSigner;
    private readonly TestOwnerAuthority _owners;
    private readonly Outbox _outbox;
    private readonly SemaphoreSlim _transitionGate = new(1, 1);
    private readonly object _hostsGate = new();
    private readonly Dictionary<string, HostAttachment> _routes = new(StringComparer.Ordinal);
    private readonly List<AuthoritativeHostRun> _retiredRuns = [];
    private TaskCompletionSource _ingressClosed = NewSignal();
    private TaskCompletionSource _oldRunReadyToRetire = NewSignal();
    private TaskCompletionSource _releaseFault = NewSignal();
    private AuthoritativeHostRun? _authoritativeRun;
    private HostAuthorityLease? _authorityLease;
    private bool _disposed;

    public HostSupervisor(
        PocDataRoot root,
        TrustedCandidateCatalogStore controlPlane,
        PointerSigner pointerSigner,
        TestOwnerAuthority owners)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _controlPlane = controlPlane ?? throw new ArgumentNullException(nameof(controlPlane));
        _pointerSigner = pointerSigner ?? throw new ArgumentNullException(nameof(pointerSigner));
        _owners = owners ?? throw new ArgumentNullException(nameof(owners));
        _outbox = new Outbox(root);
    }

    public Task<HostTransitionResult> BeginPromotionAsync(
        AuthenticatedPrincipal principal,
        string candidateId,
        HostFault fault = HostFault.None,
        CancellationToken cancellationToken = default) =>
        PromoteAsync(principal, candidateId, fault, cancellationToken);

    public Task<HostTransitionResult> PromoteAsync(
        AuthenticatedPrincipal principal,
        string candidateId,
        HostFault fault = HostFault.None,
        CancellationToken cancellationToken = default)
    {
        _ingressClosed = NewSignal();
        _oldRunReadyToRetire = NewSignal();
        _releaseFault = NewSignal();
        return TransitionAsync(
            principal,
            candidateId,
            rollback: false,
            rollbackFamily: null,
            fault: fault,
            cancellationToken: cancellationToken);
    }

    public Task<HostTransitionResult> RollbackAsync(
        AuthenticatedPrincipal principal,
        CandidateFamilyId family,
        HostFault fault = HostFault.None,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(principal);
        _ingressClosed = NewSignal();
        _oldRunReadyToRetire = NewSignal();
        _releaseFault = NewSignal();
        return TransitionAsync(
            principal,
            candidateId: null,
            rollback: true,
            rollbackFamily: family,
            fault: fault,
            cancellationToken: cancellationToken);
    }

    public Task<HostAttachment> CurrentAsync(
        AuthenticatedPrincipal principal,
        CandidateFamilyId family,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(principal);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_hostsGate)
        {
            return Task.FromResult(_routes.TryGetValue(Key(principal.OwnerId, family), out var host)
                ? host
                : throw new KeyNotFoundException("No active child is attached for the owner and family."));
        }
    }

    public async Task<HostBootResult> TryRestartActiveAsync(
        AuthenticatedPrincipal principal,
        CandidateFamilyId family,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(principal);
        PointerVerificationResult verified;
        try
        {
            verified = await _controlPlane.VerifyActivePointerAsync(
                principal,
                family,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            await FenceCurrentRunAfterUnsafeBootVerificationAsync(CancellationToken.None);
            return new HostBootResult(
                false,
                BootFailure.CandidateVerificationFailed,
                0,
                string.Empty,
                null);
        }

        if (!verified.Succeeded)
        {
            await FenceCurrentRunAfterUnsafeBootVerificationAsync(CancellationToken.None);

            return new HostBootResult(
                false,
                ToBootFailure(verified.Failure),
                0,
                string.Empty,
                null);
        }

        await _transitionGate.WaitAsync(cancellationToken);
        PendingChild? child = null;
        AuthoritativeHostRun? oldRun = null;
        var expectedHead = CandidatePointerHead.From(verified.Pointer!);
        IReadOnlyList<TrustedCandidateRecord>? expectedActive = null;
        var oldAuthorityReleased = false;
        var reopenOld = true;
        try
        {
            if (!await EnsureAuthorityLeaseAsync(cancellationToken))
            {
                return new HostBootResult(
                    false,
                    BootFailure.HostAuthorityUnavailable,
                    0,
                    string.Empty,
                    null);
            }

            var active = await _controlPlane.ReadAllVerifiedActiveCandidatesAsync(cancellationToken);
            expectedActive = active;
            oldRun = ReadAuthoritativeRun();
            if (!ContainsActive(active, principal.OwnerId, family, verified.Pointer!.CandidateSourceHash))
            {
                reopenOld = false;
                await FenceStaleRunAsync(oldRun);
                return new HostBootResult(
                    false,
                    BootFailure.CandidateVerificationFailed,
                    0,
                    string.Empty,
                    null);
            }

            if (oldRun is not null)
            {
                oldRun.Ingress.Close();
                await oldRun.Ingress.WaitForDrainAsync(cancellationToken);
                oldAuthorityReleased = !oldRun.HasExited;
                if (!await ReleaseAuthorityIfAliveAsync(oldRun, cancellationToken))
                {
                    reopenOld = false;
                }
            }

            child = StartNormalChild(
                expectedHead,
                SourceHashes(active));
            var ready = await ReadActiveReadyAsync(child, cancellationToken);
            if (ready is null ||
                ready.ProcessId != child.Process.Id ||
                !SameSourceHashes(ready.ActiveSourceHashes, SourceHashes(active)))
            {
                reopenOld = false;
                await FenceStaleRunAsync(oldRun);
                return new HostBootResult(
                    false,
                    BootFailure.CandidateVerificationFailed,
                    0,
                    string.Empty,
                    null);
            }

            var replacement = new AuthoritativeHostRun(
                child.Process,
                child.StandardError,
                AuthorityControlToken(),
                ready.ProjectionBaseUri);
            var routes = BuildRoutes(replacement, active);
            await InstallReplacementAsync(oldRun, replacement, routes);
            reopenOld = false;
            child.Detach();
            child = null;
            var attachment = routes[Key(principal.OwnerId, family)];
            return new HostBootResult(
                true,
                BootFailure.None,
                attachment.ProcessId,
                attachment.ActiveSourceHash,
                attachment);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new HostBootResult(
                false,
                BootFailure.CandidateVerificationFailed,
                0,
                string.Empty,
                null);
        }
        finally
        {
            if (child is not null)
            {
                await child.DisposeAsync();
            }

            if (reopenOld)
            {
                if (oldRun is not null &&
                    !await IsExpectedSelectionStillVerifiedAsync(
                        principal,
                        family,
                        expectedHead,
                        expectedActive,
                        CancellationToken.None))
                {
                    reopenOld = false;
                    await FenceStaleRunAsync(oldRun);
                }

                if (reopenOld &&
                    !await RestoreOldRunForReopenAsync(
                        oldRun,
                        oldAuthorityReleased,
                        CancellationToken.None))
                {
                    reopenOld = false;
                    await FenceStaleRunAsync(oldRun);
                }

                if (reopenOld)
                {
                    oldRun?.Ingress.Reopen();
                }
            }

            await ReleaseUnusedAuthorityLeaseAsync();
            _transitionGate.Release();
        }
    }

    public Task WaitUntilIngressClosedAsync(
        AuthenticatedPrincipal principal,
        CandidateFamilyId family,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(principal);
        _ = family;
        return _ingressClosed.Task.WaitAsync(cancellationToken);
    }

    public Task WaitUntilOldRunReadyToRetireAsync(CancellationToken cancellationToken = default) =>
        _oldRunReadyToRetire.Task.WaitAsync(cancellationToken);

    public void ReleaseTestFault() => _releaseFault.TrySetResult();

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        AuthoritativeHostRun[] runs;
        HostAuthorityLease? authorityLease;
        lock (_hostsGate)
        {
            runs = _retiredRuns
                .Append(_authoritativeRun)
                .Where(run => run is not null)
                .Cast<AuthoritativeHostRun>()
                .Distinct()
                .ToArray();
            _retiredRuns.Clear();
            _authoritativeRun = null;
            _routes.Clear();
            authorityLease = _authorityLease;
            _authorityLease = null;
        }

        foreach (var run in runs)
        {
            await run.DisposeAsync();
        }

        if (authorityLease is not null)
        {
            await authorityLease.DisposeAsync();
        }

        _transitionGate.Dispose();
    }

    private async Task<HostTransitionResult> TransitionAsync(
        AuthenticatedPrincipal principal,
        string? candidateId,
        bool rollback,
        CandidateFamilyId? rollbackFamily,
        HostFault fault,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(principal);
        await _transitionGate.WaitAsync(cancellationToken);
        PendingChild? child = null;
        AuthoritativeHostRun? oldRun = null;
        CandidatePointerHead? expectedHead = null;
        CandidateFamilyId? expectedFamily = null;
        IReadOnlyList<TrustedCandidateRecord>? expectedActive = null;
        ActiveCandidatePointer? proposed = null;
        var oldAuthorityReleased = false;
        var pointerAdvanced = false;
        var reopenOld = true;
        try
        {
            if (rollback)
            {
                if (rollbackFamily is null)
                {
                    throw new InvalidOperationException("Rollback requires a candidate family.");
                }

                var rollbackSnapshot = await _controlPlane.ReadVerifiedPointerSnapshotAsync(
                    principal,
                    rollbackFamily.Value,
                    cancellationToken);
                if (rollbackSnapshot.Pointer is null ||
                    IsZeroHash(rollbackSnapshot.Pointer.PreviousCandidateSourceHash))
                {
                    return HostTransitionResult.Failed(PromotionFailure.NoPreviousCandidate);
                }

                candidateId = rollbackSnapshot.Pointer.PreviousCandidateSourceHash;
            }

            if (string.IsNullOrWhiteSpace(candidateId))
            {
                return HostTransitionResult.Failed(PromotionFailure.CandidateNotApproved);
            }

            candidateId = candidateId.ToLowerInvariant();
            var candidate = await _controlPlane.FindCandidateAsync(candidateId, cancellationToken);
            if (candidate is null ||
                !string.Equals(candidate.OwnerId, principal.OwnerId, StringComparison.Ordinal) ||
                !await _controlPlane.ApprovalExistsAsync(candidateId, cancellationToken))
            {
                return HostTransitionResult.Failed(PromotionFailure.CandidateNotApproved);
            }

            var family = candidate.Family;
            expectedFamily = family;
            var snapshot = await _controlPlane.ReadVerifiedPointerSnapshotAsync(
                principal,
                family,
                cancellationToken);
            expectedHead = snapshot.Head;
            if (rollback &&
                (snapshot.Pointer is null ||
                    !string.Equals(
                        snapshot.Pointer.PreviousCandidateSourceHash,
                        candidate.SourceHash,
                        StringComparison.Ordinal)))
            {
                return HostTransitionResult.Failed(PromotionFailure.PointerHeadConflict);
            }

            var activeBefore = await _controlPlane.ReadAllVerifiedActiveCandidatesAsync(cancellationToken);
            expectedActive = activeBefore;
            var expectedActiveSourceHashes = ExpectedActiveSourceHashes(
                activeBefore,
                principal.OwnerId,
                family,
                candidate.SourceHash);
            if (!await EnsureAuthorityLeaseAsync(cancellationToken))
            {
                return HostTransitionResult.Failed(PromotionFailure.HostAuthorityUnavailable);
            }

            oldRun = ReadAuthoritativeRun();
            if (oldRun is not null)
            {
                oldRun.Ingress.Close();
            }

            _ingressClosed.TrySetResult();
            if (fault == HostFault.PauseAfterIngressClosedBeforeDrain)
            {
                await _releaseFault.Task.WaitAsync(cancellationToken);
            }

            if (oldRun is not null)
            {
                await oldRun.Ingress.WaitForDrainAsync(cancellationToken);
            }

            if (snapshot.Pointer is not null)
            {
                var pending = await _outbox.PendingTargetingCandidateRevisionAsync(
                    principal,
                    family,
                    snapshot.Pointer.CandidateSourceHash,
                    cancellationToken);
                if (pending.Count != 0)
                {
                    return HostTransitionResult.Failed(PromotionFailure.PendingCandidateTargetedOutbox);
                }
            }

            child = StartCandidateChild(
                candidateId,
                expectedHead,
                expectedActiveSourceHashes,
                fault);
            if (fault == HostFault.BeforeCandidateChildReady)
            {
                return HostTransitionResult.Failed(PromotionFailure.ChildPreflightFailed);
            }

            var preflight = await ReadCandidatePreflightAsync(child, cancellationToken);
            if (preflight is null ||
                !preflight.Succeeded ||
                !string.Equals(preflight.OwnerId, principal.OwnerId, StringComparison.Ordinal) ||
                !string.Equals(preflight.FamilyId, family.Value, StringComparison.Ordinal) ||
                !string.Equals(preflight.SourceHash, candidate.SourceHash, StringComparison.Ordinal) ||
                !SameSourceHashes(preflight.ActiveSourceHashes, expectedActiveSourceHashes))
            {
                if (!await IsExpectedSelectionStillVerifiedAsync(
                        principal,
                        expectedFamily,
                        expectedHead,
                        expectedActive,
                        cancellationToken))
                {
                    reopenOld = false;
                    await FenceStaleRunAsync(oldRun);
                    return HostTransitionResult.Failed(PromotionFailure.PointerHeadConflict);
                }

                var failure =
                    preflight?.Error?.Contains("verified head", StringComparison.OrdinalIgnoreCase) == true
                        ? PromotionFailure.PointerHeadConflict
                        : preflight?.Error?.Contains("incompatible", StringComparison.OrdinalIgnoreCase) == true
                            ? PromotionFailure.IncompatibleRetainedSchema
                            : PromotionFailure.CandidateVerificationFailed;
                if (failure == PromotionFailure.PointerHeadConflict)
                {
                    reopenOld = false;
                    await FenceStaleRunAsync(oldRun);
                }

                return HostTransitionResult.Failed(failure);
            }

            var currentSnapshot = await _controlPlane.ReadVerifiedPointerSnapshotAsync(
                principal,
                family,
                cancellationToken);
            if (currentSnapshot.Head != expectedHead ||
                !SameActiveSelection(
                    activeBefore,
                    await _controlPlane.ReadAllVerifiedActiveCandidatesAsync(cancellationToken)))
            {
                reopenOld = false;
                await FenceStaleRunAsync(oldRun);
                return HostTransitionResult.Failed(PromotionFailure.PointerHeadConflict);
            }

            var unsigned = rollback
                ? ActiveCandidatePointer.Rollback(expectedHead)
                : ActiveCandidatePointer.Next(expectedHead, candidate.SourceHash);
            proposed = _pointerSigner.Sign(unsigned);
            var advanced = await _controlPlane.TryAdvancePointerHeadAsync(
                expectedHead,
                proposed,
                cancellationToken);
            if (!advanced.Succeeded)
            {
                reopenOld = false;
                await FenceStaleRunAsync(oldRun);
                return HostTransitionResult.Failed(PromotionFailure.PointerHeadConflict);
            }

            pointerAdvanced = true;
            if (oldRun is not null)
            {
                oldAuthorityReleased = !oldRun.HasExited;
                if (!await ReleaseAuthorityIfAliveAsync(oldRun, cancellationToken))
                {
                    reopenOld = false;
                }
            }

            if (fault == HostFault.ForceActivationRecoveryFailure)
            {
                throw new InvalidOperationException("Test fault forces activation recovery failure.");
            }

            await child.Process.StandardInput.WriteLineAsync("activate".AsMemory(), cancellationToken);
            await child.Process.StandardInput.FlushAsync(cancellationToken);
            var ready = await ReadActiveReadyAsync(child, cancellationToken) ??
                throw new EndOfStreamException(
                    $"Candidate child exited before activation:{Environment.NewLine}{await child.StandardError}");
            var activeAfter = await _controlPlane.ReadAllVerifiedActiveCandidatesAsync(cancellationToken);
            if (ready.ProcessId != child.Process.Id ||
                !SameSourceHashes(ready.ActiveSourceHashes, expectedActiveSourceHashes) ||
                !SameSourceHashes(ready.ActiveSourceHashes, SourceHashes(activeAfter)))
            {
                throw new InvalidDataException("Candidate child activation did not match the advanced pointer selection.");
            }

            if (fault == HostFault.PauseBeforeOldRunRetirement)
            {
                _oldRunReadyToRetire.TrySetResult();
                await _releaseFault.Task.WaitAsync(cancellationToken);
            }

            var replacement = new AuthoritativeHostRun(
                child.Process,
                child.StandardError,
                AuthorityControlToken(),
                ready.ProjectionBaseUri);
            var routes = BuildRoutes(replacement, activeAfter);
            await InstallReplacementAsync(oldRun, replacement, routes);
            reopenOld = false;
            child.Detach();
            child = null;
            return HostTransitionResult.Started(routes[Key(principal.OwnerId, family)]);
        }
        catch (OperationCanceledException) when (
            pointerAdvanced &&
            expectedHead is not null &&
            proposed is not null)
        {
            var pending = child;
            child = null;
            var recovered = await RecoverAfterActivationFailureAsync(
                pending,
                oldRun,
                oldAuthorityReleased,
                expectedHead,
                proposed,
                fault == HostFault.ForceActivationRecoveryFailure,
                CancellationToken.None);
            if (!recovered)
            {
                reopenOld = false;
                await FenceStaleRunAsync(oldRun);
                return HostTransitionResult.Failed(PromotionFailure.ActivationRecoveryFailed);
            }

            try
            {
                expectedHead = (await _controlPlane.ReadVerifiedPointerSnapshotAsync(
                    principal,
                    expectedFamily!.Value,
                    CancellationToken.None)).Head;
                expectedActive = await _controlPlane.ReadAllVerifiedActiveCandidatesAsync(
                    CancellationToken.None);
            }
            catch
            {
                reopenOld = false;
                await FenceStaleRunAsync(oldRun);
                return HostTransitionResult.Failed(PromotionFailure.ActivationRecoveryFailed);
            }

            return HostTransitionResult.Failed(PromotionFailure.ActivationFailed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            if (pointerAdvanced && expectedHead is not null && proposed is not null)
            {
                var pending = child;
                child = null;
                var recovered = await RecoverAfterActivationFailureAsync(
                    pending,
                    oldRun,
                    oldAuthorityReleased,
                    expectedHead,
                    proposed,
                    fault == HostFault.ForceActivationRecoveryFailure,
                    CancellationToken.None);
                if (!recovered)
                {
                    reopenOld = false;
                    await FenceStaleRunAsync(oldRun);
                    return HostTransitionResult.Failed(PromotionFailure.ActivationRecoveryFailed);
                }

                try
                {
                    expectedHead = (await _controlPlane.ReadVerifiedPointerSnapshotAsync(
                        principal,
                        expectedFamily!.Value,
                        CancellationToken.None)).Head;
                    expectedActive = await _controlPlane.ReadAllVerifiedActiveCandidatesAsync(
                        CancellationToken.None);
                }
                catch
                {
                    reopenOld = false;
                    await FenceStaleRunAsync(oldRun);
                    return HostTransitionResult.Failed(PromotionFailure.ActivationRecoveryFailed);
                }

                return HostTransitionResult.Failed(PromotionFailure.ActivationFailed);
            }

            return HostTransitionResult.Failed(
                exception.Message.Contains("incompatible", StringComparison.OrdinalIgnoreCase)
                    ? PromotionFailure.IncompatibleRetainedSchema
                    : PromotionFailure.CandidateVerificationFailed);
        }
        finally
        {
            if (child is not null)
            {
                await child.DisposeAsync();
            }

            if (reopenOld)
            {
                if (oldRun is not null &&
                    !await IsExpectedSelectionStillVerifiedAsync(
                        principal,
                        expectedFamily,
                        expectedHead,
                        expectedActive,
                        CancellationToken.None))
                {
                    reopenOld = false;
                    await FenceStaleRunAsync(oldRun);
                }

                if (reopenOld &&
                    !await RestoreOldRunForReopenAsync(
                        oldRun,
                        oldAuthorityReleased,
                        CancellationToken.None))
                {
                    reopenOld = false;
                    await FenceStaleRunAsync(oldRun);
                }

                if (reopenOld)
                {
                    oldRun?.Ingress.Reopen();
                }
            }

            await ReleaseUnusedAuthorityLeaseAsync();
            _transitionGate.Release();
        }
    }

    private async Task<bool> RecoverAfterActivationFailureAsync(
        PendingChild? child,
        AuthoritativeHostRun? oldRun,
        bool oldAuthorityReleased,
        CandidatePointerHead expectedHead,
        ActiveCandidatePointer proposed,
        bool forceFailure,
        CancellationToken cancellationToken)
    {
        var childStopped = true;
        if (child is not null)
        {
            try
            {
                await child.DisposeAsync();
            }
            catch
            {
                childStopped = false;
            }
        }

        if (!childStopped)
        {
            return false;
        }

        if (forceFailure)
        {
            return false;
        }

        var pointerRecovered = await RecoverPointerAsync(expectedHead, proposed, cancellationToken);
        if (!pointerRecovered || oldRun is { HasExited: true })
        {
            return false;
        }

        if (oldRun is null)
        {
            return true;
        }

        if (oldRun.HasExited)
        {
            return false;
        }

        if (!oldAuthorityReleased)
        {
            return true;
        }

        try
        {
            await oldRun.ReacquireAuthorityAsync(cancellationToken);
            return !oldRun.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> ReleaseAuthorityIfAliveAsync(
        AuthoritativeHostRun run,
        CancellationToken cancellationToken)
    {
        if (run.HasExited)
        {
            return false;
        }

        try
        {
            await run.ReleaseAuthorityAsync(cancellationToken);
            return true;
        }
        catch (Exception) when (run.HasExited)
        {
            return false;
        }
    }

    private static async Task<bool> RestoreOldRunForReopenAsync(
        AuthoritativeHostRun? oldRun,
        bool oldAuthorityMayNeedReacquisition,
        CancellationToken cancellationToken)
    {
        if (oldRun is null)
        {
            return true;
        }

        if (oldRun.HasExited)
        {
            return false;
        }

        if (!oldAuthorityMayNeedReacquisition)
        {
            return true;
        }

        try
        {
            await oldRun.ReacquireAuthorityAsync(cancellationToken);
            return !oldRun.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> RecoverPointerAsync(
        CandidatePointerHead expectedHead,
        ActiveCandidatePointer proposed,
        CancellationToken cancellationToken)
    {
        try
        {
            if (expectedHead.Version == 0)
            {
                return await _controlPlane.TryRestoreCanonicalEmptyPointerHeadAsync(
                    proposed,
                    cancellationToken);
            }

            var compensation = _pointerSigner.Sign(ActiveCandidatePointer.Rollback(
                CandidatePointerHead.From(proposed)));
            return (await _controlPlane.TryAdvancePointerHeadAsync(
                CandidatePointerHead.From(proposed),
                compensation,
                cancellationToken)).Succeeded;
        }
        catch
        {
            return false;
        }
    }

    private async Task InstallReplacementAsync(
        AuthoritativeHostRun? oldRun,
        AuthoritativeHostRun replacement,
        IReadOnlyDictionary<string, HostAttachment> routes)
    {
        if (oldRun is not null)
        {
            await oldRun.StopAsync();
        }

        lock (_hostsGate)
        {
            foreach (var attachment in _routes.Values)
            {
                attachment.Retire();
            }

            if (oldRun is not null)
            {
                _retiredRuns.Add(oldRun);
            }

            _routes.Clear();
            foreach (var (key, attachment) in routes)
            {
                _routes.Add(key, attachment);
            }

            _authoritativeRun = replacement;
        }
    }

    private async Task FenceStaleRunAsync(AuthoritativeHostRun? staleRun)
    {
        if (staleRun is not null)
        {
            staleRun.Ingress.Close();
            await staleRun.StopAsync();
        }

        lock (_hostsGate)
        {
            if (!ReferenceEquals(_authoritativeRun, staleRun))
            {
                return;
            }

            foreach (var attachment in _routes.Values)
            {
                attachment.Retire();
            }

            _routes.Clear();
            _authoritativeRun = null;
            if (staleRun is not null && !_retiredRuns.Contains(staleRun))
            {
                _retiredRuns.Add(staleRun);
            }
        }
    }

    private Dictionary<string, HostAttachment> BuildRoutes(
        AuthoritativeHostRun run,
        IReadOnlyList<TrustedCandidateRecord> active)
    {
        var routes = new Dictionary<string, HostAttachment>(StringComparer.Ordinal);
        foreach (var candidate in active)
        {
            var family = CandidateFamilyId.Parse(candidate.FamilyId);
            var key = Key(candidate.OwnerId, family);
            routes.Add(key, new HostAttachment(run, candidate.OwnerId, family, candidate.SourceHash));
        }

        return routes;
    }

    private AuthoritativeHostRun? ReadAuthoritativeRun()
    {
        lock (_hostsGate)
        {
            return _authoritativeRun;
        }
    }

    private async Task<bool> EnsureAuthorityLeaseAsync(CancellationToken cancellationToken)
    {
        lock (_hostsGate)
        {
            if (_authorityLease is not null)
            {
                return true;
            }
        }

        var acquired = await HostAuthorityLease.TryAcquireAsync(_root, cancellationToken);
        if (acquired is null)
        {
            return false;
        }

        lock (_hostsGate)
        {
            if (_authorityLease is null)
            {
                _authorityLease = acquired;
                return true;
            }
        }

        await acquired.DisposeAsync();
        return true;
    }

    private async Task FenceCurrentRunAfterUnsafeBootVerificationAsync(
        CancellationToken cancellationToken)
    {
        await _transitionGate.WaitAsync(cancellationToken);
        try
        {
            await FenceStaleRunAsync(ReadAuthoritativeRun());
        }
        finally
        {
            await ReleaseUnusedAuthorityLeaseAsync();
            _transitionGate.Release();
        }
    }

    private string AuthorityControlToken()
    {
        lock (_hostsGate)
        {
            return _authorityLease?.ControlToken ?? throw new InvalidOperationException(
                "The authoritative child requires a supervisor handoff capability.");
        }
    }

    private async Task ReleaseUnusedAuthorityLeaseAsync()
    {
        HostAuthorityLease? unused = null;
        lock (_hostsGate)
        {
            if (_authoritativeRun is null)
            {
                unused = _authorityLease;
                _authorityLease = null;
            }
        }

        if (unused is not null)
        {
            await unused.DisposeAsync();
        }
    }

    private PendingChild StartCandidateChild(
        string candidateId,
        CandidatePointerHead expectedHead,
        IReadOnlyList<string> expectedActiveSourceHashes,
        HostFault fault)
    {
        var executable = FindFixtureHostExecutable();
        var startInfo = CreateStartInfo(executable);
        startInfo.ArgumentList.Add("--candidate-preflight");
        startInfo.ArgumentList.Add(_root.RootPath);
        startInfo.ArgumentList.Add(_root.ControlPlaneRoot);
        AddTrustedEnvironment(startInfo, candidateId, expectedHead, expectedActiveSourceHashes);
        startInfo.Environment[ActiveHostBootstrap.PreflightExpectedHeadEnvironment] =
            JsonSerializer.Serialize(expectedHead, JsonOptions);
        startInfo.Environment[ActiveHostBootstrap.PreflightSelectionEnvironment] =
            JsonSerializer.Serialize(expectedActiveSourceHashes, JsonOptions);
        if (fault is HostFault.AfterPointerAdvanceBeforeActivation or
            HostFault.AfterAuthorityReleaseBeforeAcknowledgement or
            HostFault.AfterGeneratedLocalOutboxCommitBeforeForwarderAcknowledgement or
            HostFault.AfterTrustedFanOutCommitBeforeRuleAcknowledgement or
            HostFault.AfterChartNeuronCommitBeforeUpstreamOutboxAcknowledgement)
        {
            startInfo.Environment[ActiveHostBootstrap.TestFaultEnvironment] = fault.ToString();
        }

        var process = Process.Start(startInfo) ??
            throw new InvalidOperationException("Could not start the candidate preflight child.");
        return new PendingChild(process);
    }

    private void AddTrustedEnvironment(
        ProcessStartInfo startInfo,
        string? candidateId,
        CandidatePointerHead expectedHead,
        IReadOnlyList<string> expectedActiveSourceHashes)
    {
        startInfo.Environment[ActiveHostBootstrap.AttestationKeyEnvironment] =
            _owners.AttestationPublicKey;
        startInfo.Environment[ActiveHostBootstrap.ApprovalKeyEnvironment] =
            _owners.ApprovalPublicKey;
        startInfo.Environment[ActiveHostBootstrap.PointerKeyEnvironment] =
            _owners.PointerPublicKey;
        startInfo.Environment[ActiveHostBootstrap.SessionsEnvironment] =
            JsonSerializer.Serialize(_owners.ExportSessions(), JsonOptions);
        var authorityLease = _authorityLease ?? throw new InvalidOperationException(
            "The supervisor must own the host-authority lease before it starts a child.");
        authorityLease.AddChildControlToken(startInfo.Environment);
        var delegation = _pointerSigner.SignHostAuthorityDelegation(
            new HostAuthorityDelegationPayload(
                _root.RunId,
                expectedHead.CurrentPayloadHash,
                PointerSigner.ActiveSelectionHash(expectedActiveSourceHashes)));
        startInfo.Environment[ActiveHostBootstrap.AuthorityDelegationEnvironment] =
            JsonSerializer.Serialize(delegation, JsonOptions);
        startInfo.Environment[ActiveHostBootstrap.PreflightExpectedHeadEnvironment] =
            JsonSerializer.Serialize(expectedHead, JsonOptions);
        if (candidateId is not null)
        {
            startInfo.Environment[ActiveHostBootstrap.PreflightCandidateEnvironment] = candidateId;
        }
    }

    private static ProcessStartInfo CreateStartInfo(string executable) =>
        new(executable)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
        };

    private PendingChild StartNormalChild(
        CandidatePointerHead expectedHead,
        IReadOnlyList<string> expectedActiveSourceHashes)
    {
        var executable = FindNormalHostExecutable();
        var startInfo = CreateStartInfo(executable);
        startInfo.ArgumentList.Add(_root.RootPath);
        startInfo.ArgumentList.Add(_root.ControlPlaneRoot);
        AddTrustedEnvironment(startInfo, null, expectedHead, expectedActiveSourceHashes);
        var process = Process.Start(startInfo) ??
            throw new InvalidOperationException("Could not start the pointer-selected active host.");
        return new PendingChild(process);
    }

    private static async Task<CandidatePreflightResult?> ReadCandidatePreflightAsync(
        PendingChild child,
        CancellationToken cancellationToken)
    {
        var line = await child.Process.StandardOutput.ReadLineAsync(cancellationToken);
        return line is null
            ? null
            : JsonSerializer.Deserialize<CandidatePreflightResult>(line, JsonOptions);
    }

    private static async Task<ActiveHostReady?> ReadActiveReadyAsync(
        PendingChild child,
        CancellationToken cancellationToken)
    {
        var line = await child.Process.StandardOutput.ReadLineAsync(cancellationToken);
        return line is null
            ? null
            : JsonSerializer.Deserialize<ActiveHostReady>(line, JsonOptions);
    }

    private static bool ContainsActive(
        IReadOnlyList<TrustedCandidateRecord> active,
        string ownerId,
        CandidateFamilyId family,
        string sourceHash) =>
        active.Any(candidate =>
            string.Equals(candidate.OwnerId, ownerId, StringComparison.Ordinal) &&
            string.Equals(candidate.FamilyId, family.Value, StringComparison.Ordinal) &&
            string.Equals(candidate.SourceHash, sourceHash, StringComparison.Ordinal));

    private static bool SameActiveSelection(
        IReadOnlyList<TrustedCandidateRecord> left,
        IReadOnlyList<TrustedCandidateRecord> right) =>
        SelectionKeys(left).SequenceEqual(SelectionKeys(right), StringComparer.Ordinal);

    private async Task<bool> IsExpectedSelectionStillVerifiedAsync(
        AuthenticatedPrincipal principal,
        CandidateFamilyId? family,
        CandidatePointerHead? expectedHead,
        IReadOnlyList<TrustedCandidateRecord>? expectedActive,
        CancellationToken cancellationToken)
    {
        if (family is null || expectedHead is null || expectedActive is null)
        {
            return false;
        }

        try
        {
            var current = await _controlPlane.ReadVerifiedPointerSnapshotAsync(
                principal,
                family.Value,
                cancellationToken);
            return current.Head == expectedHead &&
                SameActiveSelection(
                    expectedActive,
                    await _controlPlane.ReadAllVerifiedActiveCandidatesAsync(cancellationToken));
        }
        catch
        {
            return false;
        }
    }

    private static string[] SourceHashes(IReadOnlyList<TrustedCandidateRecord> active) =>
        active.Select(candidate => candidate.SourceHash.ToLowerInvariant())
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static string[] ExpectedActiveSourceHashes(
        IReadOnlyList<TrustedCandidateRecord> active,
        string ownerId,
        CandidateFamilyId family,
        string candidateSourceHash) =>
        active.Where(candidate =>
                !string.Equals(candidate.OwnerId, ownerId, StringComparison.Ordinal) ||
                !string.Equals(candidate.FamilyId, family.Value, StringComparison.Ordinal))
            .Select(candidate => candidate.SourceHash.ToLowerInvariant())
            .Append(candidateSourceHash.ToLowerInvariant())
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static bool SameSourceHashes(IEnumerable<string> left, IEnumerable<string> right) =>
        left.Select(value => value.ToLowerInvariant())
            .Order(StringComparer.Ordinal)
            .SequenceEqual(
                right.Select(value => value.ToLowerInvariant()).Order(StringComparer.Ordinal),
                StringComparer.Ordinal);

    private static IEnumerable<string> SelectionKeys(IReadOnlyList<TrustedCandidateRecord> active) =>
        active.Select(candidate => string.Join(
                "\n",
                candidate.OwnerId,
                candidate.FamilyId,
                candidate.SourceHash.ToLowerInvariant()))
            .Order(StringComparer.Ordinal);

    private static BootFailure ToBootFailure(PointerVerificationFailure failure) =>
        failure switch
        {
            PointerVerificationFailure.Missing => BootFailure.NoActivePointer,
            PointerVerificationFailure.InvalidSignature => BootFailure.InvalidPointerSignature,
            PointerVerificationFailure.StaleOrReplayed => BootFailure.StaleOrReplayedPointer,
            _ => BootFailure.CandidateVerificationFailed,
        };

    private static string Key(string ownerId, CandidateFamilyId family) =>
        ownerId + "\n" + family.Value;

    private static bool IsZeroHash(string value) =>
        string.Equals(value, new string('0', 64), StringComparison.Ordinal);

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static string FindFixtureHostExecutable() => FindHostExecutable(
        "DigitalBrain.Poc.Acceptance.FixtureHost",
        "tests");

    private static string FindNormalHostExecutable() => FindHostExecutable(
        "DigitalBrain.Poc.Host",
        "src");

    private static string FindHostExecutable(string project, string parent)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var poc = Path.Combine(current.FullName, "poc", "DigitalBrain.Poc.slnx");
            if (File.Exists(poc))
            {
                var executable = OperatingSystem.IsWindows() ? $"{project}.exe" : project;
                var path = Path.Combine(
                    current.FullName,
                    "poc",
                    parent,
                    project,
                    "bin",
                    "Release",
                    "net11.0",
                    executable);
                return File.Exists(path)
                    ? path
                    : throw new FileNotFoundException("The POC host apphost was not built.", path);
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not find the POC host apphost.");
    }

    private sealed record CandidatePreflightResult(
        bool Succeeded,
        int ProcessId,
        string OwnerId,
        string FamilyId,
        string SourceHash,
        string[] ActiveSourceHashes,
        string? Error);

    private sealed record ActiveHostReady(
        int ProcessId,
        string[] ActiveSourceHashes,
        Uri ProjectionBaseUri);

    private sealed class PendingChild : IAsyncDisposable
    {
        private bool _detached;

        public PendingChild(Process process)
        {
            Process = process;
            StandardError = process.StandardError.ReadToEndAsync();
        }

        public Process Process { get; }

        public Task<string> StandardError { get; }

        public void Detach() => _detached = true;

        public async ValueTask DisposeAsync()
        {
            if (_detached)
            {
                return;
            }

            try
            {
                if (!Process.HasExited)
                {
                    try
                    {
                        Process.Kill(entireProcessTree: true);
                    }
                    catch (InvalidOperationException) when (Process.HasExited)
                    {
                    }
                }

                if (!Process.HasExited)
                {
                    await Process.WaitForExitAsync();
                }
            }
            catch (InvalidOperationException) when (Process.HasExited)
            {
            }
            finally
            {
                Process.Dispose();
            }
        }
    }
}
