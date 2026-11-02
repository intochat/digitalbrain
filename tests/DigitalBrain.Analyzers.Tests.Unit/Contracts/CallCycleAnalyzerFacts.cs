using System.Threading.Tasks;
using DigitalBrain.Analyzers.Contracts;
using Xunit;

namespace DigitalBrain.Analyzers.Tests.Unit.Contracts;

public sealed class CallCycleAnalyzerFacts
{
    [Fact]
    public Task Direct_synchronous_cycle_reports_DB0033() => Verify<CallCycleAnalyzer>.Diagnostics("""
        using System.Threading.Tasks;
        using DigitalBrain;

        public interface IA { Task Ping(IB b); }
        public interface IB { Task Pong(IA a); }

        public sealed class NeuronA : Neuron, IA
        {
            public async Task Ping(IB b) => await {|DB0033:b.Pong(this)|};
        }

        public sealed class NeuronB : Neuron, IB
        {
            public async Task Pong(IA a) => await {|DB0033:a.Ping(this)|};
        }
        """);

    [Fact]
    public Task Asynchronous_oneway_notification_loop_reports_nothing() => Verify<CallCycleAnalyzer>.Diagnostics("""
        using System.Threading.Tasks;
        using DigitalBrain;

        public interface IA { Task Ping(IB b); }
        public interface IB
        {
            [OneWayCall]
            Task Pong(IA a);
        }

        public sealed class NeuronA : Neuron, IA
        {
            public async Task Ping(IB b) => await b.Pong(this);
        }

        public sealed class NeuronB : Neuron, IB
        {
            public async Task Pong(IA a) => await a.Ping(this);
        }
        """);

    [Fact]
    public Task Linear_chain_without_cycle_reports_nothing() => Verify<CallCycleAnalyzer>.Diagnostics("""
        using System.Threading.Tasks;
        using DigitalBrain;

        public interface IA { Task M1(IB b); }
        public interface IB { Task M2(IC c); }
        public interface IC { Task M3(); }

        public sealed class NeuronA : Neuron, IA
        {
            public async Task M1(IB b) => await b.M2(null!);
        }

        public sealed class NeuronB : Neuron, IB
        {
            public async Task M2(IC c) => await c.M3();
        }

        public sealed class NeuronC : Neuron, IC
        {
            public Task M3() => Task.CompletedTask;
        }
        """);
}
