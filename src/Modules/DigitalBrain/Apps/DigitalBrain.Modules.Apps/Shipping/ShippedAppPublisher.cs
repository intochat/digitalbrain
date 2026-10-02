using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using DigitalBrain.Apps;
using DigitalBrain.Contracts;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Core.Enforcement;

namespace DigitalBrain.Apps;

// Brings every shipped app to the marketplace at startup: commit it when its folder changed, verify
// the revision's scenarios once, and publish it only when they pass. A red app stays unpublished and
// its failing steps are visible on its spec page.
internal sealed class ShippedAppPublisher(AppPublishing publishing, IEnumerable<IShippedAppSource> sources, IHostApplicationLifetime lifetime, IConfiguration configuration, ILogger<ShippedAppPublisher> logger) : BackgroundService
{
    // Verifying a shipped app runs real sandbox scripts. A test host declares what it needs:
    // "true" (default) ships everything, "false" nothing, and a comma-separated list of package
    // names ships only those.
    public const string ShipOnStartupKey = "DigitalBrain:Apps:ShipOnStartup";


    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var selection = Selection(configuration[ShipOnStartupKey]);
        if (selection is { Count: 0 })
        {
            logger.LogInformation("Shipped apps stay unpublished: {Key} is off.", ShipOnStartupKey);
            return;
        }
        if (!lifetime.ApplicationStarted.IsCancellationRequested)
        {
            var started = new TaskCompletionSource();
            await using var registration = lifetime.ApplicationStarted.Register(started.SetResult);
            await started.Task.WaitAsync(stoppingToken);
        }
        foreach (var source in sources)
        {
            foreach (var app in source.Load())
            {
                stoppingToken.ThrowIfCancellationRequested();
                if (selection is not null && !selection.Contains(app.Package.Name)) { continue; }
                if (app.Package.Owner != source.Publisher) { throw new InvalidDataException("Shipped package owner must match its source publisher."); }
                CallerContextStamper.Stamp(new CallerContext
                {
                    PrincipalId = source.Publisher,
                    AccountId = source.Publisher,
                    BrainId = source.Publisher,
                    Kind = CallerKind.Platform,
                    StampedBy = TrustedEdge.Platform,
                });
                try { await Ship(app).WaitAsync(stoppingToken); }
                catch (Exception error) when (error is not OperationCanceledException)
                { logger.LogError(error, "Shipping {Package} failed.", app.Package); }
            }
        }
    }
    private async Task Ship(ShippedApp app)
    {
        var verification = await publishing.Publish(app.Package, app.Content, $"Ship {app.Content.Manifest.Title}");
        if (!verification.Green)
        { logger.LogWarning("{Package} stays unpublished: verification failed.", app.Package); }
    }

    // null means every package; an empty set means none.
    private static HashSet<string>? Selection(string? configured) => configured?.Trim().ToLowerInvariant() switch
    {
        null or "" or "true" => null,
        "false" => [],
        var names => [.. names.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)],
    };

}
