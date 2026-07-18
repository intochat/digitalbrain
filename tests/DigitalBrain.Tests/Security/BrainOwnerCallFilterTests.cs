using System.Reflection;
using System.Runtime.CompilerServices;
using DigitalBrain;
using DigitalBrain.Kernel;
using DigitalBrain.Tests.Kernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Orleans.Journaling;
using Orleans.Runtime;
using Orleans.Serialization.Invocation;
using Orleans.TestingHost;
using Xunit;

namespace DigitalBrain.Tests.Security;

public sealed class BrainOwnerCallFilterTests
{
    [Fact]
    public async Task Incoming_filter_allows_system_target_IRemindable_ReceiveReminder_without_owner()
    {
        RequestContext.Remove(nameof(BrainOwnerId));
        var invoked = false;
        var filter = new BrainOwnerIncomingCallFilter();
        var context = new RecordingIncomingCallContext(
            grain: UninitializedNeuron(),
            sourceId: SystemReminderSourceId(),
            interfaceMethod: typeof(IRemindable).GetMethod(nameof(IRemindable.ReceiveReminder))!,
            onInvoke: () =>
            {
                invoked = true;
                return Task.CompletedTask;
            });

        await filter.Invoke(context);

        Assert.True(invoked);
    }

    [Fact]
    public async Task Incoming_filter_denies_ReceiveReminder_from_non_system_source_without_owner()
    {
        RequestContext.Remove(nameof(BrainOwnerId));
        var filter = new BrainOwnerIncomingCallFilter();
        var context = new RecordingIncomingCallContext(
            grain: UninitializedNeuron(),
            sourceId: GrainId.Create("client", "caller"),
            interfaceMethod: typeof(IRemindable).GetMethod(nameof(IRemindable.ReceiveReminder))!,
            onInvoke: static () => Task.CompletedTask);

        var denied = await Assert.ThrowsAsync<BrainException>(() => filter.Invoke(context));
        Assert.Equal(NeuronFailureKind.AuthenticationRequired, denied.FailureKind);
    }

    [Fact]
    public async Task Incoming_filter_denies_system_target_business_method_without_owner()
    {
        RequestContext.Remove(nameof(BrainOwnerId));
        var filter = new BrainOwnerIncomingCallFilter();
        var context = new RecordingIncomingCallContext(
            grain: UninitializedNeuron(),
            sourceId: SystemReminderSourceId(),
            interfaceMethod: typeof(ITestNeuron).GetMethod(nameof(ITestNeuron.ReadStatusAsync))!,
            onInvoke: static () => Task.CompletedTask);

        var denied = await Assert.ThrowsAsync<BrainException>(() => filter.Invoke(context));
        Assert.Equal(NeuronFailureKind.AuthenticationRequired, denied.FailureKind);
    }

    [Fact]
    public async Task Outgoing_filter_exposes_typed_owner_during_invoke_when_Current_is_set()
    {
        var ownerContext = new BrainOwnerContext();
        ownerContext.Current = new BrainOwnerId("owner-a");
        var filter = new BrainOwnerOutgoingCallFilter(ownerContext);
        object? observed = null;

        try
        {
            await filter.Invoke(new RecordingOutgoingCallContext(() =>
            {
                observed = RequestContext.Get(nameof(BrainOwnerId));
                return Task.CompletedTask;
            }));

            Assert.Equal(new BrainOwnerId("owner-a"), Assert.IsType<BrainOwnerId>(observed));
        }
        finally
        {
            ownerContext.Current = null;
            RequestContext.Remove(nameof(BrainOwnerId));
        }
    }

    [Fact]
    public async Task Outgoing_filter_does_not_expose_stale_owner_when_Current_is_null()
    {
        var ownerContext = new BrainOwnerContext();
        ownerContext.Current = null;
        RequestContext.Set(nameof(BrainOwnerId), new BrainOwnerId("stale-owner"));
        var filter = new BrainOwnerOutgoingCallFilter(ownerContext);
        object? observed = "sentinel";

        try
        {
            await filter.Invoke(new RecordingOutgoingCallContext(() =>
            {
                observed = RequestContext.Get(nameof(BrainOwnerId));
                return Task.CompletedTask;
            }));

            Assert.Null(observed);
        }
        finally
        {
            ownerContext.Current = null;
            RequestContext.Remove(nameof(BrainOwnerId));
        }
    }

    [Fact]
    public void Independent_BrainOwnerContext_instances_do_not_share_Current()
    {
        var first = new BrainOwnerContext();
        var second = new BrainOwnerContext();

        try
        {
            first.Current = new BrainOwnerId("owner-a");
            second.Current = new BrainOwnerId("owner-b");

            Assert.Equal(new BrainOwnerId("owner-a"), first.Current);
            Assert.Equal(new BrainOwnerId("owner-b"), second.Current);

            first.Current = null;

            Assert.Null(first.Current);
            Assert.Equal(new BrainOwnerId("owner-b"), second.Current);
        }
        finally
        {
            first.Current = null;
            second.Current = null;
            RequestContext.Remove(nameof(BrainOwnerId));
        }
    }

    [Fact]
    public async Task Incoming_filter_allows_matching_owner_and_denies_cross_owner_raw_grain_access()
    {
        await using var cluster = await OwnerFilterCluster.CreateAsync();
        var ownerContext = cluster.ServiceProvider.GetRequiredService<BrainOwnerContext>();
        ownerContext.Current = new BrainOwnerId("owner-a");

        try
        {
            var ownNeuron = new DigitalBrainClient(cluster.Client, new BrainOwnerId("owner-a")).Get<ITestNeuron>();
            await ownNeuron.WriteStatusAsync(NeuronStatus.Active);
            Assert.Equal(NeuronStatus.Active, await ownNeuron.ReadStatusAsync());

            var foreignNeuron = cluster.Client.GetGrain<ITestNeuron>("owner-b");
            var denied = await Assert.ThrowsAsync<BrainException>(() => foreignNeuron.ReadStatusAsync());
            Assert.Equal(NeuronFailureKind.AuthorizationDenied, denied.FailureKind);
        }
        finally
        {
            ownerContext.Current = null;
            RequestContext.Remove(nameof(BrainOwnerId));
        }
    }

    [Fact]
    public async Task Incoming_filter_requires_authenticated_owner_for_neuron_calls()
    {
        await using var cluster = await OwnerFilterCluster.CreateAsync();
        var ownerContext = cluster.ServiceProvider.GetRequiredService<BrainOwnerContext>();
        ownerContext.Current = null;

        try
        {
            var unauthenticated = cluster.Client.GetGrain<ITestNeuron>("owner-a");
            var missing = await Assert.ThrowsAsync<BrainException>(() => unauthenticated.ReadStatusAsync());
            Assert.Equal(NeuronFailureKind.AuthenticationRequired, missing.FailureKind);
        }
        finally
        {
            ownerContext.Current = null;
            RequestContext.Remove(nameof(BrainOwnerId));
        }
    }

    private static Neuron UninitializedNeuron() =>
        (Neuron)RuntimeHelpers.GetUninitializedObject(typeof(TestNeuron));

    private static GrainId SystemReminderSourceId() =>
        SystemTargetGrainId.Create(
            SystemTargetGrainId.CreateGrainType("reminder"),
            SiloAddress.Zero).GrainId;
}

file sealed class RecordingIncomingCallContext(
    object grain,
    GrainId? sourceId,
    MethodInfo interfaceMethod,
    Func<Task> onInvoke) : IIncomingGrainCallContext
{
    public IGrainContext TargetContext => throw new NotSupportedException();
    public MethodInfo ImplementationMethod => interfaceMethod;
    public IInvokable Request => throw new NotSupportedException();
    public object Grain => grain;
    public GrainId? SourceId => sourceId;
    public GrainId TargetId => default;
    public GrainInterfaceType InterfaceType => default;
    public string InterfaceName => interfaceMethod.DeclaringType?.FullName ?? string.Empty;
    public string MethodName => interfaceMethod.Name;
    public MethodInfo InterfaceMethod => interfaceMethod;
    public object? Result { get; set; }
    public Response? Response { get; set; }
    public Task Invoke() => onInvoke();
}

file sealed class RecordingOutgoingCallContext(Func<Task> onInvoke) : IOutgoingGrainCallContext
{
    public IGrainContext? SourceContext => null;
    public IInvokable Request => throw new NotSupportedException();
    public object Grain => throw new NotSupportedException();
    public GrainId? SourceId => null;
    public GrainId TargetId => default;
    public GrainInterfaceType InterfaceType => default;
    public string InterfaceName => string.Empty;
    public string MethodName => string.Empty;
    public MethodInfo InterfaceMethod => throw new NotSupportedException();
    public object? Result { get; set; }
    public Response? Response { get; set; }
    public Task Invoke() => onInvoke();
}

file static class OwnerFilterCluster
{
    public static async Task<TestCluster> CreateAsync()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<OwnerFilterSiloConfigurator>();
        builder.AddClientBuilderConfigurator<OwnerFilterClientConfigurator>();
        var cluster = builder.Build();
        await cluster.DeployAsync();
        return cluster;
    }

    private sealed class OwnerFilterSiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            siloBuilder.AddJournalStorage();
            siloBuilder.Services.AddSingleton<IJournalStorageProvider>(new VolatileJournalStorageProvider());
            siloBuilder.AddBrainKernel();
        }
    }

    private sealed class OwnerFilterClientConfigurator : IClientBuilderConfigurator
    {
        public void Configure(IConfiguration configuration, IClientBuilder clientBuilder) =>
            clientBuilder.AddDigitalBrainClient();
    }
}
