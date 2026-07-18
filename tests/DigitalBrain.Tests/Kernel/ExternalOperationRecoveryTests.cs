using DigitalBrain;
using DigitalBrain.Kernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Orleans.Journaling;
using Orleans.Runtime;
using Orleans.TestingHost;
using Xunit;

namespace DigitalBrain.Tests.Kernel;

public sealed class ExternalOperationRecoveryTests
{
    [Fact]
    public async Task Pending_is_durably_written_before_test_external_function_runs()
    {
        await using var cluster = await RecoveryCluster.CreateAsync();
        using var owner = OwnerContext.Push("owner-pending");
        var operationId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

        var neuron = cluster.GrainFactory.GetGrain<ITestNeuron>("owner-pending");
        await neuron.ExecuteTestExternalAsync(operationId, TestExternalMode.Succeed);

        var probe = await neuron.ReadExternalProbeAsync(operationId);
        Assert.NotNull(probe);
        Assert.Equal(ExternalOperationStatus.Pending, probe.StatusAtInvoke);
        Assert.True(probe.PendingWasReadableAtInvoke);
        Assert.True(probe.EffectRecorded);

        var stored = await neuron.ReadOperationAsync(operationId);
        Assert.NotNull(stored);
        Assert.Equal(ExternalOperationStatus.Succeeded, stored.Status);
        Assert.Null(stored.FailureKind);
    }

    [Fact]
    public async Task Crash_after_effect_before_outcome_recovers_as_Unknown()
    {
        await using var cluster = await RecoveryCluster.CreateAsync();
        using var owner = OwnerContext.Push("owner-crash");
        var operationId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

        var neuron = cluster.GrainFactory.GetGrain<ITestNeuron>("owner-crash");
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => neuron.ExecuteTestExternalAsync(operationId, TestExternalMode.CrashBeforeOutcome));

        var probe = await neuron.ReadExternalProbeAsync(operationId);
        Assert.NotNull(probe);
        Assert.True(probe.EffectRecorded);
        var pending = await neuron.ReadOperationAsync(operationId);
        Assert.NotNull(pending);
        Assert.Equal(ExternalOperationStatus.Pending, pending.Status);

        await neuron.ForceDeactivateAsync();
        await Task.Delay(500);

        var recovered = cluster.GrainFactory.GetGrain<ITestNeuron>("owner-crash");
        var after = await recovered.ReadOperationAsync(operationId);
        Assert.NotNull(after);
        Assert.Equal(ExternalOperationStatus.Unknown, after.Status);
        Assert.Equal(NeuronFailureKind.OperationUnknown, after.FailureKind);
    }

    [Fact]
    public async Task Idempotent_reconciliation_moves_Unknown_to_Succeeded_from_provider_receipt()
    {
        await using var cluster = await RecoveryCluster.CreateAsync();
        using var owner = OwnerContext.Push("owner-reconcile");
        var operationId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

        var neuron = cluster.GrainFactory.GetGrain<ITestNeuron>("owner-reconcile");
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => neuron.ExecuteTestExternalAsync(operationId, TestExternalMode.CrashBeforeOutcome));
        await neuron.ForceDeactivateAsync();
        await Task.Delay(500);

        var recovered = cluster.GrainFactory.GetGrain<ITestNeuron>("owner-reconcile");
        var unknown = await recovered.ReadOperationAsync(operationId);
        Assert.NotNull(unknown);
        Assert.Equal(ExternalOperationStatus.Unknown, unknown.Status);

        await recovered.ReconcileFromProviderReceiptAsync(operationId);
        var first = await recovered.ReadOperationAsync(operationId);
        Assert.NotNull(first);
        Assert.Equal(ExternalOperationStatus.Succeeded, first.Status);
        Assert.Null(first.FailureKind);

        await recovered.ReconcileFromProviderReceiptAsync(operationId);
        var second = await recovered.ReadOperationAsync(operationId);
        Assert.NotNull(second);
        Assert.Equal(ExternalOperationStatus.Succeeded, second.Status);
    }

    [Fact]
    public async Task Provider_and_operation_failures_map_to_typed_failure_kinds()
    {
        await using var cluster = await RecoveryCluster.CreateAsync();
        using var owner = OwnerContext.Push("owner-fail");

        var neuron = cluster.GrainFactory.GetGrain<ITestNeuron>("owner-fail");
        var providerId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        var operationId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

        await neuron.ExecuteTestExternalAsync(providerId, TestExternalMode.FailProvider);
        var provider = await neuron.ReadOperationAsync(providerId);
        Assert.NotNull(provider);
        Assert.Equal(ExternalOperationStatus.Failed, provider.Status);
        Assert.Equal(NeuronFailureKind.ProviderUnavailable, provider.FailureKind);

        await neuron.ExecuteTestExternalAsync(operationId, TestExternalMode.FailOperation);
        var failed = await neuron.ReadOperationAsync(operationId);
        Assert.NotNull(failed);
        Assert.Equal(ExternalOperationStatus.Failed, failed.Status);
        Assert.Equal(NeuronFailureKind.OperationFailed, failed.FailureKind);
    }

    [Fact]
    public void Operation_transitions_use_enum_and_record_validation_not_string_switches()
    {
        var pending = new ExternalOperation(
            Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
            ExternalOperationStatus.Pending,
            FailureKind: null);

        var succeeded = ExternalOperationTransitions.Apply(
            pending,
            new ExternalOperationTransition.Succeeded());
        Assert.Equal(ExternalOperationStatus.Succeeded, succeeded.Status);

        var failed = ExternalOperationTransitions.Apply(
            pending,
            new ExternalOperationTransition.Failed(NeuronFailureKind.ProviderUnavailable));
        Assert.Equal(ExternalOperationStatus.Failed, failed.Status);
        Assert.Equal(NeuronFailureKind.ProviderUnavailable, failed.FailureKind);

        var unknown = ExternalOperationTransitions.Apply(
            pending,
            new ExternalOperationTransition.Unknown(NeuronFailureKind.OperationUnknown));
        Assert.Equal(ExternalOperationStatus.Unknown, unknown.Status);

        var reconciled = ExternalOperationTransitions.Apply(
            unknown,
            new ExternalOperationTransition.ReconcileSucceeded());
        Assert.Equal(ExternalOperationStatus.Succeeded, reconciled.Status);

        Assert.Throws<InvalidOperationException>(() =>
            ExternalOperationTransitions.Apply(
                succeeded,
                new ExternalOperationTransition.Failed(NeuronFailureKind.OperationFailed)));
    }
}

file static class RecoveryCluster
{
    public static async Task<TestCluster> CreateAsync()
    {
        var builder = new TestClusterBuilder();
        builder.Options.InitialSilosCount = 1;
        builder.AddSiloBuilderConfigurator<SiloConfigurator>();
        builder.AddClientBuilderConfigurator<ClientConfigurator>();
        var cluster = builder.Build();
        await cluster.DeployAsync();
        return cluster;
    }

    private sealed class SiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            siloBuilder.AddJournalStorage();
            siloBuilder.Services.AddSingleton<IJournalStorageProvider>(new VolatileJournalStorageProvider());
            siloBuilder.AddBrainKernel();
            siloBuilder.UseInMemoryReminderService();
            siloBuilder.AddMemoryGrainStorage("PubSubStore");
            siloBuilder.AddMemoryStreams(nameof(NeuronNotification), _ => { });
        }
    }

    private sealed class ClientConfigurator : IClientBuilderConfigurator
    {
        public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
        {
            clientBuilder.AddMemoryStreams(nameof(NeuronNotification), _ => { });
        }
    }
}

internal static class OwnerContext
{
    public static IDisposable Push(string owner)
    {
        var prior = RequestContext.Get(nameof(BrainOwnerId));
        RequestContext.Set(nameof(BrainOwnerId), new BrainOwnerId(owner));
        return new Reset(prior);
    }

    private sealed class Reset(object? prior) : IDisposable
    {
        public void Dispose()
        {
            if (prior is null)
                RequestContext.Remove(nameof(BrainOwnerId));
            else
                RequestContext.Set(nameof(BrainOwnerId), prior);
        }
    }
}
