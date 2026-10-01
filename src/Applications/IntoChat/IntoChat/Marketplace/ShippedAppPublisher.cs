using System.Text.Json;
using DigitalBrain.Apps;
using DigitalBrain.Contracts;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Core.Enforcement;

namespace IntoChat.Marketplace;

// Brings every shipped app to the marketplace at startup: commit it when its folder changed, verify
// the revision's scenarios once, and publish it only when they pass. A red app stays unpublished and
// its failing steps are visible on its spec page.
internal sealed class ShippedAppPublisher(IDigitalBrain brain, MarketplaceService marketplace, IHostApplicationLifetime lifetime, IConfiguration configuration, ILogger<ShippedAppPublisher> logger) : BackgroundService
{
    // Verifying a shipped app runs real sandbox scripts. A test host declares what it needs:
    // "true" (default) ships everything, "false" nothing, and a comma-separated list of package
    // names ships only those.
    public const string ShipOnStartupKey = "DigitalBrain:Apps:ShipOnStartup";

    private static readonly JsonSerializerOptions CanonicalJson = new(JsonSerializerDefaults.Web);

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
        CallerContextStamper.Stamp(new CallerContext
        {
            PrincipalId = ShippedApps.Publisher,
            AccountId = ShippedApps.Publisher,
            BrainId = ShippedApps.Publisher,
            Kind = CallerKind.Platform,
            StampedBy = TrustedEdge.Platform,
        });
        foreach (var app in ShippedApps.Load())
        {
            if (stoppingToken.IsCancellationRequested) { return; }
            if (selection is not null && !selection.Contains(app.Package.Name)) { continue; }
            try { await Ship(app); }
            catch (Exception error) when (error is not OperationCanceledException)
            { logger.LogError(error, "Shipping {Package} failed.", app.Package); }
        }
    }

    private async Task Ship(ShippedApp app)
    {
        var package = brain.Get<IPackage>(app.Package.ToString());
        var snapshot = await package.Read();
        var revisionId = snapshot.Head;
        if (revisionId is null || !Same((await package.ReadRevision(revisionId)).Content, app.Content))
        {
            revisionId = (await package.Commit(new CommitPackage(Guid.NewGuid(), snapshot.Head, app.Content, $"Ship {app.Content.Manifest.Title}"))).Id;
            logger.LogInformation("Committed {Package}@{Revision}.", app.Package, revisionId);
        }
        var revision = new PackageRevisionRef(app.Package, revisionId);
        var verifier = brain.Get<IAppVerification>(IAppVerification.Key(revision));
        var verification = await verifier.Read();
        if (verification is null)
        {
            await marketplace.RequireRunnable(revision);
            verification = await verifier.Verify();
        }
        if (!verification.Green)
        {
            logger.LogWarning("{Package}@{Revision} stays unpublished: {Failed} of {Total} scenarios did not pass.", app.Package, revisionId,
                verification.Run.Scenarios.Count(scenario => !scenario.Passed), verification.Run.Scenarios.Length);
            return;
        }
        if (snapshot.Published != revisionId)
        {
            await package.Publish(new PublishPackage(Guid.NewGuid(), revisionId));
            logger.LogInformation("Published {Package}@{Revision}.", app.Package, revisionId);
        }
    }

    // null means every package; an empty set means none.
    private static HashSet<string>? Selection(string? configured) => configured?.Trim().ToLowerInvariant() switch
    {
        null or "" or "true" => null,
        "false" => [],
        var names => [.. names.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)],
    };

    private static bool Same(PackageContent left, PackageContent right) => Canonical(left) == Canonical(right);

    private static string Canonical(PackageContent content) => JsonSerializer.Serialize(new
    {
        content.Manifest,
        content.Source,
        files = new SortedDictionary<string, string>(content.Files?.ToDictionary() ?? [], StringComparer.Ordinal),
    }, CanonicalJson);
}


