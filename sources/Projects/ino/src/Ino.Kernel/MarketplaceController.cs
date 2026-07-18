using System.Text.Json;
using Ino.Aspire.Hosting;
using Ino.Core;
using Ino.Core.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;

namespace Ino.Kernel;

[ApiController]
[Route("marketplace")]
public sealed class MarketplaceController(
    IOptions<MarketplaceControllerOptions> options,
    IDomainRestartService restartService,
    IGrainFactory grains,
    ILogger<MarketplaceController> logger) : ControllerBase
{
    private static readonly SemaphoreSlim InstallLock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new DomainIdJsonConverter(), new NeuronIdJsonConverter() },
    };

    [HttpGet("available")]
    public ActionResult<MarketplaceFeed> GetAvailable()
    {
        var feed = LoadFeed();
        return Ok(feed);
    }

    [HttpGet("available/{id}")]
    public ActionResult<MarketplaceFeedEntry> GetAvailableById(string id)
    {
        var domainId = DomainId.From(id);
        var feed = LoadFeed();
        var entry = feed.Domains.FirstOrDefault(e => e.Id == domainId);
        if (entry is null) return NotFound(new { status = "not_found", id });
        return Ok(entry);
    }

    [HttpGet("installed")]
    public ActionResult GetInstalled()
    {
        var installed = InstalledSet.Load(options.Value.InstalledStatePath);
        return Ok(new { installed = installed.Select(b => b.Value).ToArray() });
    }

    [HttpPost("install/{id}")]
    public async Task<ActionResult> Install(string id, CancellationToken ct)
    {
        var domainId = DomainId.From(id);

        await InstallLock.WaitAsync(ct);
        try
        {
            var feed = LoadFeed();
            if (!feed.Domains.Any(e => e.Id == domainId))
                return NotFound(new { status = "not_found", id });

            var installed = InstalledSet.Load(options.Value.InstalledStatePath);
            if (installed.Contains(domainId))
                return Conflict(new { status = "already_installed", id });

            installed.Add(domainId);
            try
            {
                InstalledSet.Save(installed, options.Value.InstalledStatePath);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "installed.json write failed during install of {Id}", id);
                return StatusCode(500, new { status = "state_write_failed", detail = ex.Message });
            }

            RestartOutcome outcome;
            try
            {
                outcome = await restartService.RestartDomainsAsync(options.Value.RestartTimeout, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "domains silo restart failed after install of {Id}", id);
                CompensateFailedInstall(domainId, id);
                return StatusCode(504, new { status = "restart_failed", detail = ex.Message });
            }

            var status = outcome == RestartOutcome.Restarted ? "installed" : "installed_pending_restart";
            return Ok(new { status, installed = installed.Select(b => b.Value).ToArray() });
        }
        finally
        {
            InstallLock.Release();
        }
    }

    [HttpPost("install/{id}/consent")]
    public ActionResult Consent(string id)
    {
        return StatusCode(501, new { status = "not_implemented", phase = "Phase 5" });
    }

    [HttpPost("uninstall/{id}")]
    public async Task<ActionResult> Uninstall(string id, CancellationToken ct)
    {
        var domainId = DomainId.From(id);

        await InstallLock.WaitAsync(ct);
        try
        {
            var installed = InstalledSet.Load(options.Value.InstalledStatePath);
            if (!installed.Contains(domainId))
                return NotFound(new { status = "not_installed", id });

            installed.Remove(domainId);
            try
            {
                InstalledSet.Save(installed, options.Value.InstalledStatePath);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "installed.json write failed during uninstall of {Id}", id);
                return StatusCode(500, new { status = "state_write_failed", detail = ex.Message });
            }

            RestartOutcome outcome;
            try
            {
                outcome = await restartService.RestartDomainsAsync(options.Value.RestartTimeout, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "domains silo restart failed after uninstall of {Id}", id);
                CompensateFailedUninstall(domainId, id);
                return StatusCode(504, new { status = "restart_failed", detail = ex.Message });
            }

            var status = outcome == RestartOutcome.Restarted ? "uninstalled" : "uninstalled_pending_restart";
            return Ok(new { status, installed = installed.Select(b => b.Value).ToArray() });
        }
        finally
        {
            InstallLock.Release();
        }
    }

    [HttpGet("installed/{id}/neurons")]
    public async Task<ActionResult> GetInstalledNeurons(string id, CancellationToken ct)
    {
        var domainId = DomainId.From(id);
        var installed = InstalledSet.Load(options.Value.InstalledStatePath);
        if (!installed.Contains(domainId))
            return NotFound(new { status = "not_installed", id });

        var discovery = grains.GetDiscovery();
        var dump = await discovery.DumpAsync(ct);
        var synapseTypesForDomain = dump.Canonical
            .Where(c => c.Domain == domainId)
            .Select(c => c.SynapseType)
            .ToHashSet();

        var neurons = await discovery.DumpNeuronsAsync(ct);
        var scoped = neurons
            .Where(e => synapseTypesForDomain.Contains(e.CanonicalSynapseType))
            .Select(e => new
            {
                id = e.Id.Value,
                displayName = e.DisplayName,
                description = e.Description,
            })
            .ToArray();

        return Ok(new { domainId = domainId.Value, neurons = scoped });
    }

    [HttpGet("/discovery/table")]
    public async Task<ActionResult> DiscoveryTable(CancellationToken ct)
    {
        var dump = await grains.GetDiscovery().DumpAsync(ct);
        // Project System.Type to string — System.Text.Json cannot serialize
        // Type safely and an empty-dump shape check should not require a
        // custom converter.
        var payload = new
        {
            canonical = dump.Canonical
                .Select(c => new
                {
                    synapseType = c.SynapseType.FullName,
                    grainType = c.GrainType.FullName,
                    domain = c.Domain.Value,
                })
                .ToArray(),
            reactive = dump.Reactive
                .Select(r => new
                {
                    synapseType = r.SynapseType.FullName,
                    grainType = r.GrainType.FullName,
                    domain = r.Domain.Value,
                })
                .ToArray(),
            countsBySilo = dump.CountsBySilo,
        };
        return Ok(payload);
    }

    // Restart failed after installed.json was persisted. Roll the state back
    // so a retry sees a clean slate; a second failure here is logged but not
    // raised — the restart-failure response to the caller takes precedence.
    private void CompensateFailedInstall(DomainId domainId, string id)
    {
        try
        {
            var reverted = InstalledSet.Load(options.Value.InstalledStatePath);
            if (!reverted.Remove(domainId)) return;
            InstalledSet.Save(reverted, options.Value.InstalledStatePath);
            logger.LogWarning("Reverted installed.json after failed restart for install of {Id}.", id);
        }
        catch (Exception compensateEx)
        {
            logger.LogError(compensateEx,
                "Compensating write failed after restart failure for install of {Id}. installed.json may be inconsistent.",
                id);
        }
    }

    private void CompensateFailedUninstall(DomainId domainId, string id)
    {
        try
        {
            var reverted = InstalledSet.Load(options.Value.InstalledStatePath);
            if (!reverted.Add(domainId)) return;
            InstalledSet.Save(reverted, options.Value.InstalledStatePath);
            logger.LogWarning("Reverted installed.json after failed restart for uninstall of {Id}.", id);
        }
        catch (Exception compensateEx)
        {
            logger.LogError(compensateEx,
                "Compensating write failed after restart failure for uninstall of {Id}. installed.json may be inconsistent.",
                id);
        }
    }

    private MarketplaceFeed LoadFeed()
    {
        var path = options.Value.MarketplaceFeedPath;
        if (!global::System.IO.File.Exists(path))
        {
            logger.LogWarning(
                "Marketplace feed not found at {ResolvedPath}. Returning empty feed — " +
                "all /marketplace/available and install lookups will report no domains.",
                global::System.IO.Path.GetFullPath(path));
            return new MarketplaceFeed(Array.Empty<MarketplaceFeedEntry>());
        }

        var json = global::System.IO.File.ReadAllText(path);
        return JsonSerializer.Deserialize<MarketplaceFeed>(json, JsonOptions)
               ?? new MarketplaceFeed(Array.Empty<MarketplaceFeedEntry>());
    }
}
