using DigitalBrain.Confirmation;
using DigitalBrain.Kernel;
using DigitalBrain.Neurons;
using DigitalBrain.Operations;
using DigitalBrain.Runtime.Orleans.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

#pragma warning disable CA1859

namespace DigitalBrain.Runtime.Orleans.Tests.Unit.Adversarial;

public sealed class HostNeuronSeatAdversarialFacts
{
    private static readonly BrainId Brain1 = new("brain-1");
    private static readonly BrainId Brain2 = new("brain-2");
    private static readonly NeuronId Neuron1A = new(Brain1, "worker", "1");
    private static readonly NeuronId Neuron1B = new(Brain1, "worker", "2");
    private static readonly NeuronId Neuron2A = new(Brain2, "worker", "1");

    #region 1. RuntimeServicesHosting NeuronBinding DI Resolution & Isolation

    [Fact]
    public void RuntimeServicesHosting_NeuronBindings_PerActivationCachingAndIsolation()
    {
        var services = new ServiceCollection();

        // Register bindings matching RuntimeServicesHosting
        services.AddSingleton(new NeuronBinding(typeof(IOperations), (sp, id) => new NeuronOperations(sp, id.Brain, id.ToString())));
        services.AddSingleton(new NeuronBinding(typeof(IOperationsQuery), (sp, id) => new NeuronOperationsQuery(sp, id.Brain)));
        services.AddSingleton(new NeuronBinding(typeof(IConfirmations), (sp, id) => DummyConfirmations.Instance));

        var sp = services.BuildServiceProvider();
        var bindings = sp.GetServices<NeuronBinding>();

        var activation1A = new NeuronServices(sp, Neuron1A, bindings);
        var activation1B = new NeuronServices(sp, Neuron1B, bindings);
        var activation2A = new NeuronServices(sp, Neuron2A, bindings);

        // 1. Caching: Multiple GetService calls return identical cached instance
        var op1A_first = activation1A.GetService(typeof(IOperations));
        var op1A_second = activation1A.GetService(typeof(IOperations));
        Assert.NotNull(op1A_first);
        Assert.Same(op1A_first, op1A_second);
        Assert.IsType<NeuronOperations>(op1A_first);

        // 2. Isolation between activations of same brain
        var op1B = activation1B.GetService(typeof(IOperations));
        Assert.NotNull(op1B);
        Assert.NotSame(op1A_first, op1B);

        // 3. Isolation between activations across different brains
        var op2A = activation2A.GetService(typeof(IOperations));
        Assert.NotNull(op2A);
        Assert.NotSame(op1A_first, op2A);
        Assert.NotSame(op1B, op2A);

        // 4. IOperationsQuery caching and cross-brain isolation
        var query1A = activation1A.GetService(typeof(IOperationsQuery));
        var query1A_repeat = activation1A.GetService(typeof(IOperationsQuery));
        var query2A = activation2A.GetService(typeof(IOperationsQuery));

        Assert.NotNull(query1A);
        Assert.Same(query1A, query1A_repeat);
        Assert.NotSame(query1A, query2A);

        // 5. IConfirmations caching
        var conf1A = activation1A.GetService(typeof(IConfirmations));
        var conf1A_repeat = activation1A.GetService(typeof(IConfirmations));
        Assert.NotNull(conf1A);
        Assert.Same(conf1A, conf1A_repeat);
        Assert.Same(DummyConfirmations.Instance, conf1A);
    }

    private sealed class DummyConfirmations : IConfirmations
    {
        public static DummyConfirmations Instance { get; } = new();
        public ConfirmationPolicy Policy => ConfirmationPolicy.DestructiveOnly;
        public Task<ConfirmationResult> Confirm(ConfirmationPlan plan, CancellationToken ct = default) => throw new NotSupportedException();
        public Task Resolve(string planId, bool approve, CancellationToken ct = default) => throw new NotSupportedException();
    }

    [Fact]
    public void HostNeuronContext_ImplementsINeuronContextAndINeuronSeat()
    {
        var contextType = typeof(HostNeuronContext);
        var ifaces = contextType.GetInterfaces();

        Assert.Contains(typeof(INeuronContext), ifaces);
        Assert.Contains(ifaces, iface => iface.Name == "INeuronSeat");

        // Verify INeuronSeat method signatures on HostNeuronContext
        var seatInterface = ifaces.First(iface => iface.Name == "INeuronSeat");
        var loadMethod = seatInterface.GetMethod("Load");
        var saveMethod = seatInterface.GetMethod("Save");
        var clearMethod = seatInterface.GetMethod("Clear");
        var servicesProp = seatInterface.GetProperty("Services");

        Assert.NotNull(loadMethod);
        Assert.NotNull(saveMethod);
        Assert.NotNull(clearMethod);
        Assert.NotNull(servicesProp);
    }

    [Fact]
    public void NeuronHostGrainBase_Construct_BindsViaNeuronServices()
    {
        var services = new ServiceCollection();
        var operations = new NeuronOperations(services.BuildServiceProvider(), Brain1, Neuron1A.ToString());
        services.AddSingleton(new NeuronBinding(typeof(IOperations), (sp, id) => operations));

        var sp = services.BuildServiceProvider();
        var bound = new NeuronServices(sp, Neuron1A, sp.GetServices<NeuronBinding>());

        var resolved = bound.GetService(typeof(IOperations));
        Assert.NotNull(resolved);
        Assert.Same(operations, resolved);
    }

    #endregion
}
