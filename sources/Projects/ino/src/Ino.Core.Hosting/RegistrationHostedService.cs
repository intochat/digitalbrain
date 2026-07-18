using Ino.Core.Hosting.Placement;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;
using Orleans.Runtime;
using Orleans.Runtime.MembershipService.SiloMetadata;

namespace Ino.Core.Hosting;

/// <summary>
/// Hosted service that writes this silo's INeuron&lt;T&gt; / IReactsTo&lt;T&gt; entries into the
/// cluster-wide <see cref="Discovery"/> grain. Correctness here is load-bearing: the
/// gateway's Cortex routing (slice 5) and SystemFirePort lookups (slice 3) both key off
/// Discovery, so a missing registration means every routable intent falls through to
/// <c>UnroutedIntent</c>.
///
/// Fix for slice 16 — the domains silo was racing Orleans' silo-metadata gossip: when
/// <c>StartAsync</c> fired the initial <c>RegisterAsync</c>, the local
/// <see cref="ISiloMetadataCache"/> hadn't yet received the kernel silo's
/// <c>ino.silo=kernel</c> tag, so <c>[PinToSilo("kernel")]</c>'s director filtered every
/// silo out → Orleans fell back to default placement → Discovery activated on the
/// domains silo instead of the kernel silo. The domains-silo activation got our
/// write; the kernel silo's subsequent reads hit a different, empty activation.
///
/// Two changes prevent the recurrence:
/// 1. We wait until a silo with <c>ino.silo=kernel</c> metadata appears in the membership
///    snapshot + metadata cache before calling Discovery for the first time, so
///    PinToSilo has a target when the grain activates.
/// 2. We subscribe to <see cref="IClusterMembershipService.MembershipUpdates"/> and
///    re-fire registration each time the cluster composition changes, so a kernel-silo
///    restart (post-v0.1 rolling deploys) rebuilds our entries without operator action.
/// </summary>
public sealed class RegistrationHostedService(
    IGrainFactory grains,
    IClusterMembershipService clusterMembership,
    ISiloMetadataCache siloMetadata,
    IOptions<RegistrationOptions> options,
    ILogger<RegistrationHostedService> logger) : IHostedService
{
    static readonly TimeSpan KernelSiloWaitTimeout = TimeSpan.FromSeconds(60);
    static readonly TimeSpan KernelSiloPollInterval = TimeSpan.FromMilliseconds(250);
    const string KernelSiloRole = "kernel";
    const int MaxRegisterAttempts = 5;

    Task? _loop;
    CancellationTokenSource? _cts;

    public Task StartAsync(CancellationToken ct)
    {
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => RegisterLoopAsync(_cts.Token), ct);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken ct)
    {
        _cts?.Cancel();
        if (_loop is not null)
        {
            try
            {
                await _loop.WaitAsync(ct);
            }
            catch (OperationCanceledException) { }
        }
    }

    async Task RegisterLoopAsync(CancellationToken ct)
    {
        var registration = DomainRegistrar.Build(options.Value);

        try
        {
            await WaitForKernelSiloVisibleAsync(ct);
            await TryRegisterAsync(registration, reason: "initial", ct);

            // Each membership update is an opportunity: kernel silo restart, new silo
            // joining, old one leaving. RegisterAsync is idempotent (clears this silo's
            // entries first, then re-adds), so firing on every update is safe.
            await foreach (var snapshot in clusterMembership.MembershipUpdates.WithCancellation(ct))
            {
                await TryRegisterAsync(registration, reason: $"membership-v{snapshot.Version}", ct);
            }
        }
        catch (OperationCanceledException) { /* shutdown */ }
        catch (Exception ex)
        {
            logger.LogError(ex, "Registration loop terminated unexpectedly for silo {Silo}", options.Value.Silo);
        }
    }

    async Task WaitForKernelSiloVisibleAsync(CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow + KernelSiloWaitTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (KernelSiloVisible())
            {
                return;
            }
            await Task.Delay(KernelSiloPollInterval, ct);
        }
        logger.LogWarning("Timed out waiting for kernel silo to appear in membership after {Timeout}; " +
            "firing registration anyway — PinToSilo fallback may leave Discovery on the wrong silo", KernelSiloWaitTimeout);
    }

    bool KernelSiloVisible()
    {
        var snapshot = clusterMembership.CurrentSnapshot;
        foreach (var member in snapshot.Members.Values)
        {
            if (member.Status != SiloStatus.Active) continue;
            var metadata = siloMetadata.GetSiloMetadata(member.SiloAddress);
            if (metadata is null) continue;
            if (metadata.Metadata.TryGetValue(PinToSiloStrategy.SiloMetadataKey, out var role)
                && role == KernelSiloRole)
            {
                return true;
            }
        }
        return false;
    }

    async Task TryRegisterAsync(SiloRegistration registration, string reason, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= MaxRegisterAttempts; attempt++)
        {
            try
            {
                await grains.GetDiscovery().RegisterAsync(registration, ct);
                logger.LogInformation(
                    "Registered {Canonical} canonical + {Reactive} reactive targets for silo {Silo} ({Reason}, attempt {Attempt})",
                    registration.Canonical.Count, registration.Reactive.Count, options.Value.Silo, reason, attempt);
                return;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (attempt < MaxRegisterAttempts)
            {
                logger.LogWarning(ex,
                    "Registration attempt {Attempt} failed ({Reason}); retrying",
                    attempt, reason);
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(5, attempt)), ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Registration attempts exhausted ({Reason}); next membership update will retry",
                    reason);
            }
        }
    }
}
