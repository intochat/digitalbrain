using DigitalBrain.Runtime.Marketplace;
using DigitalBrain.Runtime.Neurons;
using Orleans.Journaling;

namespace DigitalBrain.Kernel.Runtime.Neurons;

/// <summary>
/// Simulates the central database storage layer (Postgres DB) modeled entirely as a stateful Kernel Neuron.
/// </summary>
[GrainType("DigitalBrain.Kernel.Runtime.Neurons.PostgresDbNeuron")]
[ImplicitStreamSubscription(nameof(PostgresDbNeuron))]
public sealed class PostgresDbNeuron(
    [FromKeyedServices("incoming")] IDurableList<Synapse> incoming,
    [FromKeyedServices("outgoing")] IDurableList<Synapse> outgoing,
    [FromKeyedServices("db-bundles")] IDurableList<BundleInfo> bundles,
    [FromKeyedServices("db-purchases")] IDurableList<PurchaseRow> purchases,
    [FromKeyedServices("db-licenses")] IDurableList<LicenseRow> licenses,
    IGrainFactory grains,
    ILogger<PostgresDbNeuron> logger)
    : Neuron(incoming, outgoing, grains, logger), IPostgresDbNeuron
{
    // ==========================================
    // DIRECT IN- silos COORDINATED METHODS
    // ==========================================

    public async Task InsertBundleAsync(BundleInfo bundle)
    {
        // Prevent duplicates
        var existing = bundles.FirstOrDefault(b => b.BundleId == bundle.BundleId && b.Version == bundle.Version);
        if (existing is not null)
        {
            bundles.Remove(existing);
        }

        bundles.Add(bundle);
        await WriteStateAsync();
        Logger.LogInformation("Persisted bundle to database: {BundleId} ({Version})", bundle.BundleId, bundle.Version);
    }

    public Task<List<BundleInfo>> SelectBundlesAsync()
    {
        return Task.FromResult(bundles.ToList());
    }

    public async Task InsertPurchaseAsync(PurchaseRow purchase)
    {
        purchases.Add(purchase);
        await WriteStateAsync();
        Logger.LogInformation("Persisted purchase to database: {PurchaseId} for {UserId}", purchase.PurchaseId, purchase.UserId);
    }

    public Task<List<PurchaseRow>> SelectPurchasesAsync(string userId, string bundleId)
    {
        var result = purchases
            .Where(p => p.UserId == userId && p.BundleId == bundleId)
            .ToList();
        return Task.FromResult(result);
    }

    public async Task InsertLicenseAsync(LicenseRow license)
    {
        // Keep only active license
        var existing = licenses.FirstOrDefault(l => l.UserId == license.UserId && l.BundleId == license.BundleId);
        if (existing is not null)
        {
            licenses.Remove(existing);
        }

        licenses.Add(license);
        await WriteStateAsync();
        Logger.LogInformation("Persisted license grant to database for {UserId} on {BundleId}", license.UserId, license.BundleId);
    }

    public Task<List<LicenseRow>> SelectLicensesAsync(string userId, string bundleId)
    {
        var result = licenses
            .Where(l => l.UserId == userId && l.BundleId == bundleId)
            .ToList();
        return Task.FromResult(result);
    }

    // ==========================================
    // SYNAPSE STREAM SIGNAL HANDLERS
    // ==========================================

    protected override async Task HandleSynapseAsync(Synapse s)
    {
        switch (s)
        {
            case DbInsertBundle insert:
                await InsertBundleAsync(insert.Bundle);
                await FireSynapseAsync(new DbInsertBundleReply(Success: true) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: insert.CorrelationId,
            causationId: insert.SynapseId,
            callerNeuronId: InstanceId,
            callerNeuronType: nameof(PostgresDbNeuron),
            receiverNeuronId: insert.CallerNeuronId,
            receiverNeuronType: insert.CallerNeuronType ?? string.Empty,
            timestamp: DateTimeOffset.UtcNow
        ) });
                break;

            case DbSelectBundles select:
                var bList = await SelectBundlesAsync();
                await FireSynapseAsync(new DbSelectBundlesReply(Bundles: bList) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: select.CorrelationId,
            causationId: select.SynapseId,
            callerNeuronId: InstanceId,
            callerNeuronType: nameof(PostgresDbNeuron),
            receiverNeuronId: select.CallerNeuronId,
            receiverNeuronType: select.CallerNeuronType ?? string.Empty,
            timestamp: DateTimeOffset.UtcNow
        ) });
                break;

            case DbInsertPurchase purchase:
                await InsertPurchaseAsync(purchase.Purchase);
                await FireSynapseAsync(new DbInsertPurchaseReply(Success: true) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: purchase.CorrelationId,
            causationId: purchase.SynapseId,
            callerNeuronId: InstanceId,
            callerNeuronType: nameof(PostgresDbNeuron),
            receiverNeuronId: purchase.CallerNeuronId,
            receiverNeuronType: purchase.CallerNeuronType ?? string.Empty,
            timestamp: DateTimeOffset.UtcNow
        ) });
                break;

            case DbInsertLicense license:
                await InsertLicenseAsync(license.License);
                await FireSynapseAsync(new DbInsertLicenseReply(Success: true) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: license.CorrelationId,
            causationId: license.SynapseId,
            callerNeuronId: InstanceId,
            callerNeuronType: nameof(PostgresDbNeuron),
            receiverNeuronId: license.CallerNeuronId,
            receiverNeuronType: license.CallerNeuronType ?? string.Empty,
            timestamp: DateTimeOffset.UtcNow
        ) });
                break;

            case DbSelectLicenses selectLicenses:
                var lList = await SelectLicensesAsync(selectLicenses.UserId, selectLicenses.BundleId);
                await FireSynapseAsync(new DbSelectLicensesReply(Licenses: lList) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: selectLicenses.CorrelationId,
            causationId: selectLicenses.SynapseId,
            callerNeuronId: InstanceId,
            callerNeuronType: nameof(PostgresDbNeuron),
            receiverNeuronId: selectLicenses.CallerNeuronId,
            receiverNeuronType: selectLicenses.CallerNeuronType ?? string.Empty,
            timestamp: DateTimeOffset.UtcNow
        ) });
                break;
        }
    }
}
