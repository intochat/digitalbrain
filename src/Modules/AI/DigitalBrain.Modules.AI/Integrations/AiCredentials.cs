using System.Collections.Concurrent;
using DigitalBrain.Contracts;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Sdk.Integrations;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.AI;

[GenerateSerializer, Alias("db.ai.provider-unavailable")]
public sealed class ProviderUnavailableException : InvalidOperationException
{
    public ProviderUnavailableException(string integration, string status, string[] missing)
        : base($"Integration '{integration}' is {status}"
            + (missing.Length == 0 ? "" : $"; missing {string.Join(", ", missing)}")
            + $". An operator completes it through POST /integrations/{integration}/registration.")
    {
        Integration = integration;
        Status = status;
        Missing = missing;
    }

    [Id(0)] public string Integration { get; }

    [Id(1)] public string Status { get; }

    [Id(2)] public string[] Missing { get; }
}

internal interface IAiCredentials
{
    RegistrationSnapshot StatusOf(string integrationId);

    // Changes only when the registration actually changed (status, settings or a rotated secret), so holders of a released key know to rebuild.
    long GenerationOf(string integrationId);

    string ReleaseSecret(string integrationId, string field);

    Task<string> ReleaseSecretAsync(string integrationId, string field, CancellationToken cancellationToken = default);
}

// Sync callers (client construction, provider selection) read a short-lived cache of the registration
// status; the secret itself is never cached and leaves the vault only inside ReleaseSecret*.
internal sealed class RegistrationCredentials(IGrainFactory grains, TimeProvider time, ILogger<RegistrationCredentials> logger) : IAiCredentials
{
    private static readonly TimeSpan StatusLifetime = TimeSpan.FromSeconds(5);

    private static readonly CallerContext PlatformCaller = new()
    {
        PrincipalId = "ai",
        AccountId = "ai",
        BrainId = "ai",
        Kind = CallerKind.Platform,
        StampedBy = TrustedEdge.Platform,
        AppId = "ai",
    };

    private readonly ConcurrentDictionary<string, (RegistrationSnapshot Snapshot, DateTimeOffset ReadAt, long Generation)> _statuses = new(StringComparer.Ordinal);
    private long _generation;

    public long GenerationOf(string integrationId)
    {
        StatusOf(integrationId);
        return _statuses.TryGetValue(integrationId, out var cached) ? cached.Generation : 0;
    }

    public string ReleaseSecret(string integrationId, string field)
        => Task.Run(() => ReleaseSecretAsync(integrationId, field)).GetAwaiter().GetResult();

    public async Task<string> ReleaseSecretAsync(string integrationId, string field, CancellationToken cancellationToken = default)
    {
        var snapshot = await Registration(integrationId).Read().WaitAsync(cancellationToken).ConfigureAwait(false);
        if (snapshot.Status != RegistrationStatus.Ready)
        {
            throw new ProviderUnavailableException(integrationId, snapshot.Status.ToString(), snapshot.MissingFields);
        }

        var released = await Registration(integrationId).Release(PlatformCaller).WaitAsync(cancellationToken).ConfigureAwait(false);
        return released.Values[field];
    }

    private static bool SameRegistration(RegistrationSnapshot before, RegistrationSnapshot after)
        => before.Status == after.Status && before.Revision == after.Revision
            && before.MissingFields.SequenceEqual(after.MissingFields, StringComparer.Ordinal)
            && before.Settings.Count == after.Settings.Count
            && before.Settings.All(pair => after.Settings.TryGetValue(pair.Key, out var value) && value == pair.Value);

    private IIntegrationRegistration Registration(string integrationId) => grains.GetGrain<IIntegrationRegistration>("integration/" + integrationId);

    public RegistrationSnapshot StatusOf(string integrationId)
    {
        var now = time.GetUtcNow();
        if (_statuses.TryGetValue(integrationId, out var cached) && now - cached.ReadAt < StatusLifetime)
        {
            return cached.Snapshot;
        }

        RegistrationSnapshot snapshot;
        try
        {
            snapshot = Task.Run(() => Registration(integrationId).Read()).GetAwaiter().GetResult();
        }
        catch (Exception error)
        {
            logger.LogWarning(error, "Integration {IntegrationId} registration could not be read; treating it as unconfigured.", integrationId);
            snapshot = new RegistrationSnapshot { IntegrationId = integrationId, Status = RegistrationStatus.Unconfigured };
        }

        var generation = _statuses.TryGetValue(integrationId, out var previous) && SameRegistration(previous.Snapshot, snapshot)
            ? previous.Generation
            : Interlocked.Increment(ref _generation);
        _statuses[integrationId] = (snapshot, now, generation);
        return snapshot;
    }
}

// Test seam for hosts that build provider selection without a brain: fixed readiness, endpoints and keys.
internal sealed class FixedAiCredentials : IAiCredentials
{
    private readonly Dictionary<string, (string ApiKey, string? Endpoint)> _ready = new(StringComparer.Ordinal);

    public FixedAiCredentials Ready(string integrationId, string apiKey, string? endpoint = null)
    {
        _ready[integrationId] = (apiKey, endpoint);
        return this;
    }

    public long GenerationOf(string integrationId) => 0;

    public RegistrationSnapshot StatusOf(string integrationId)
        => _ready.TryGetValue(integrationId, out var entry)
            ? new RegistrationSnapshot
            {
                IntegrationId = integrationId,
                Status = RegistrationStatus.Ready,
                Settings = entry.Endpoint is null ? [] : new() { [AiIntegrations.EndpointField] = entry.Endpoint },
            }
            : new RegistrationSnapshot { IntegrationId = integrationId, Status = RegistrationStatus.Unconfigured, MissingFields = [AiIntegrations.ApiKeyField] };

    public string ReleaseSecret(string integrationId, string field)
        => _ready.TryGetValue(integrationId, out var entry)
            ? entry.ApiKey
            : throw new ProviderUnavailableException(integrationId, RegistrationStatus.Unconfigured.ToString(), [field]);

    public Task<string> ReleaseSecretAsync(string integrationId, string field, CancellationToken cancellationToken = default)
        => Task.FromResult(ReleaseSecret(integrationId, field));
}

internal static class AiCredentialsExtensions
{
    internal static bool IsReady(this IAiCredentials credentials, string integrationId)
        => credentials.StatusOf(integrationId).Status == RegistrationStatus.Ready;

    internal static string? Setting(this IAiCredentials credentials, string integrationId, string field)
        => credentials.StatusOf(integrationId).Settings.TryGetValue(field, out var value) ? value : null;

    internal static void RequireReady(this IAiCredentials credentials, string integrationId)
    {
        var snapshot = credentials.StatusOf(integrationId);
        if (snapshot.Status != RegistrationStatus.Ready)
        {
            throw new ProviderUnavailableException(integrationId, snapshot.Status.ToString(), snapshot.MissingFields);
        }
    }
}
