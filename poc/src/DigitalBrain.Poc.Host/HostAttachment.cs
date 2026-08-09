using DigitalBrain.Poc.Abstractions;
using DigitalBrain.Poc.Charting;
using DigitalBrain.Poc.Runtime;
using DigitalBrain.Poc.Social.Contracts;

namespace DigitalBrain.Poc.Host;

public sealed class HostAttachment : IAsyncDisposable
{
    private readonly AuthoritativeHostRun _run;
    private int _retired;

    internal HostAttachment(
        AuthoritativeHostRun run,
        string ownerId,
        CandidateFamilyId family,
        string activeSourceHash)
    {
        _run = run ?? throw new ArgumentNullException(nameof(run));
        OwnerId = ownerId;
        Family = family;
        ActiveSourceHash = activeSourceHash.ToLowerInvariant();
    }

    public int ProcessId => _run.ProcessId;

    public string OwnerId { get; }

    public CandidateFamilyId Family { get; }

    public string ActiveSourceHash { get; }

    public Uri ProjectionBaseUri => _run.ProjectionBaseUri;

    internal IngressQuiesceGate Ingress => _run.Ingress;

    public IngressAdmissionLease AcquireIngressLease(OwnerSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        ThrowIfRetired();
        return _run.Ingress.Acquire((synapse, cancellationToken) =>
            FireAdmittedAsync(session, synapse, cancellationToken));
    }

    public async Task FireTrustedAsync(
        OwnerSession session,
        Synapse synapse,
        CancellationToken cancellationToken = default)
    {
        await using var lease = AcquireIngressLease(session);
        await lease.FireAsync(synapse, cancellationToken);
    }

    public Task<IReadOnlyList<string>> JournalKindsAsync(
        OwnerSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ThrowIfRetired();
        return _run.SendAsync<IReadOnlyList<string>>(
            "journal",
            new SessionWireRequest(session.Token),
            cancellationToken);
    }

    public async Task<int> ChartPointCountAsync(
        OwnerSession session,
        string chartId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ThrowIfRetired();
        return (await _run.SendAsync<IntWireResponse>(
            "chart-point-count",
            new ChartCountWireRequest(session.Token, chartId),
            cancellationToken)).Value;
    }

    public Task<ChartNeuron.Snapshot> ChartAsync(
        OwnerSession session,
        string chartId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ThrowIfRetired();
        return _run.SendAsync<ChartNeuron.Snapshot>(
            "chart",
            new ChartCountWireRequest(session.Token, chartId),
            cancellationToken);
    }

    public Task<IReadOnlyList<string>> JournalKindsForInputAsync(
        OwnerSession session,
        string receiptId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ThrowIfRetired();
        return _run.SendAsync<IReadOnlyList<string>>(
            "journal-for-input",
            new ReceiptWireRequest(session.Token, receiptId),
            cancellationToken);
    }

    public Task<IReadOnlyList<string>> OrderedLogicalJournalKindsAsync(
        OwnerSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ThrowIfRetired();
        return _run.SendAsync<IReadOnlyList<string>>(
            "logical-journal",
            new SessionWireRequest(session.Token),
            cancellationToken);
    }

    public async Task<int> GeneratedAcceptedCountAsync(
        OwnerSession session,
        CandidateFamilyId family,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ThrowIfRetired();
        return (await _run.SendAsync<IntWireResponse>(
            "generated-accepted-count",
            new FamilyWireRequest(session.Token, family.Value),
            cancellationToken)).Value;
    }

    public Task ReplayLastChartDeliveryAsync(
        OwnerSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ThrowIfRetired();
        return _run.SendAsync<object>(
            "replay-last-chart-delivery",
            new SessionWireRequest(session.Token),
            cancellationToken);
    }

    public Task WaitForExitAsync(CancellationToken cancellationToken = default) =>
        _run.WaitForExitAsync(cancellationToken);

    public ValueTask DisposeAsync()
    {
        Retire();
        return ValueTask.CompletedTask;
    }

    internal void Retire() => Interlocked.Exchange(ref _retired, 1);

    private Task FireAdmittedAsync(
        OwnerSession session,
        Synapse synapse,
        CancellationToken cancellationToken) =>
        synapse switch
        {
            SocialPostObserved social => _run.SendAsync<object>(
                "fire-social",
                new SocialWireRequest(
                    session.Token,
                    social.PostId,
                    social.Author,
                    social.OccurredAt),
                cancellationToken),
            ProbeIngress probe => _run.SendAsync<object>(
                "probe",
                new FireWireRequest(session.Token, probe.Value, probe.Value),
                cancellationToken),
            _ => throw new NotSupportedException(
                $"The active host ingress does not support '{synapse.GetType().FullName}'."),
        };

    private void ThrowIfRetired()
    {
        if (Volatile.Read(ref _retired) != 0)
        {
            throw new HostQuiescingException();
        }
    }

    private sealed record SessionWireRequest(string SessionToken);

    private sealed record FireWireRequest(string SessionToken, string ReceiptId, string? Value);

    private sealed record SocialWireRequest(
        string SessionToken,
        string PostId,
        string Author,
        DateTimeOffset OccurredAt);

    private sealed record ChartCountWireRequest(string SessionToken, string ChartId);

    private sealed record ReceiptWireRequest(string SessionToken, string ReceiptId);

    private sealed record FamilyWireRequest(string SessionToken, string Family);

    private sealed record IntWireResponse(int Value);
}
