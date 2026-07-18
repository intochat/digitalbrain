using System.Text.Json;
using DigitalBrain.Runtime.Neurons;
using Microsoft.EntityFrameworkCore;

namespace DigitalBrain.SDK.DigitalBrain.Persistence;

public sealed class EfCoreSynapsePersistenceService(IDbContextFactory<SynapseDbContext> dbContextFactory) 
    : ISynapsePersistenceService
{
    public async Task SaveSynapseAsync(Synapse synapse, CancellationToken cancellationToken = default)
    {
        using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await context.Database.EnsureCreatedAsync(cancellationToken);

        // Map abstract Synapse record properties to the flat entity
        var entity = new SynapseEntity
        {
            SynapseId = synapse.SynapseId,
            CorrelationId = synapse.CorrelationId,
            CausationId = synapse.CausationId,
            CallerNeuronId = synapse.CallerNeuronId,
            CallerNeuronType = synapse.CallerNeuronType,
            ReceiverNeuronId = synapse.ReceiverNeuronId,
            ReceiverNeuronType = synapse.ReceiverNeuronType,
            Timestamp = synapse.Timestamp,
            Traceparent = synapse.Traceparent,
            Tracestate = synapse.Tracestate,
            PayloadJson = JsonSerializer.Serialize(synapse, synapse.GetType(), new JsonSerializerOptions { WriteIndented = false })
        };

        context.Synapses.Add(entity);
        await context.SaveChangesAsync(cancellationToken);
    }
}
