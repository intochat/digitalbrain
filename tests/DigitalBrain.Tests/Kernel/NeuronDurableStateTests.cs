using Brain.Contracts;
using Brain.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Orleans.Journaling;
using Orleans.Journaling.Json;
using Orleans.Runtime;
using Orleans.TestingHost;
using Xunit;

namespace DigitalBrain.Tests.Kernel;

public sealed class NeuronDurableStateTests
{
    [Fact]
    public async Task Test_neuron_writes_and_reads_universal_durable_state_with_explicit_WriteStateAsync()
    {
        await using var cluster = await UnitTestVolatileJournalCluster.CreateAsync();
        var ownerKey = nameof(BrainOwnerId);
        var prior = RequestContext.Get(ownerKey);
        RequestContext.Set(ownerKey, new BrainOwnerId("owner-a"));

        try
        {
            var neuron = cluster.GrainFactory.GetGrain<ITestNeuron>("owner-a");

            await neuron.WriteStatusAsync(NeuronStatus.Active);

            var operationId = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var operation = new ExternalOperation(
                operationId,
                ExternalOperationStatus.Pending,
                FailureKind: null);
            await neuron.WriteOperationAsync(operationId, operation);

            var notificationId = Guid.Parse("22222222-2222-2222-2222-222222222222");
            var notification = new NeuronNotification(
                notificationId,
                operationId);
            await neuron.WriteNotificationAsync(notificationId, notification);

            Assert.Equal(NeuronStatus.Active, await neuron.ReadStatusAsync());
            Assert.Equal(operation, await neuron.ReadOperationAsync(operationId));
            Assert.Equal(notification, await neuron.ReadNotificationAsync(notificationId));
            Assert.Null(await neuron.ReadOperationAsync(Guid.NewGuid()));
            Assert.Null(await neuron.ReadNotificationAsync(Guid.NewGuid()));
        }
        finally
        {
            if (prior is null)
                RequestContext.Remove(ownerKey);
            else
                RequestContext.Set(ownerKey, prior);
        }
    }
}

file static class UnitTestVolatileJournalCluster
{
    public static async Task<TestCluster> CreateAsync()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<UnitTestVolatileJournalSiloConfigurator>();
        var cluster = builder.Build();
        await cluster.DeployAsync();
        return cluster;
    }

    private sealed class UnitTestVolatileJournalSiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            siloBuilder.AddJournalStorage();
            siloBuilder.Services.AddSingleton<IJournalStorageProvider>(new VolatileJournalStorageProvider());
            siloBuilder.AddBrainKernel();
        }
    }
}
