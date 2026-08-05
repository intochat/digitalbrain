namespace DigitalBrain.Core.Tests
{
    public sealed class CatalogBootTests
    {
        [Fact(DisplayName = "Two answerers for one question kind fail catalog boot")]
        public void DualAnswerersFailBoot()
        {
            var failure = Assert.Throws<InvalidOperationException>(
                () => Catalog.Build([typeof(FirstAnswerer), typeof(SecondAnswerer)]));

            Assert.Contains("exactly one answerer", failure.Message, StringComparison.Ordinal);
        }

        [Fact(DisplayName = "Kind collision of two neuron classes with the same simple name fails catalog boot")]
        public void KindCollisionFailsBoot()
        {
            var failure = Assert.Throws<InvalidOperationException>(
                () => Catalog.Build([typeof(CollisionLeft.Probe), typeof(CollisionRight.Probe)]));

            Assert.Contains("kind", failure.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    public sealed class FirstAnswerer : Neuron, IAnswers<Greet, Greeted>
    {
        public Task<Greeted?> HandleAsync(Greet question, CancellationToken cancellationToken)
            => Task.FromResult<Greeted?>(new("a"));
    }

    public sealed class SecondAnswerer : Neuron, IAnswers<Greet, Greeted>
    {
        public Task<Greeted?> HandleAsync(Greet question, CancellationToken cancellationToken)
            => Task.FromResult<Greeted?>(new("b"));
    }
}

namespace CollisionLeft
{
    [GrainType("collision-left-probe")]
    public sealed class Probe : DigitalBrain.Neuron, DigitalBrain.INeuron<DigitalBrain.Core.Tests.PlanDay>
    {
        public Task HandleAsync(DigitalBrain.Core.Tests.PlanDay fact, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}

namespace CollisionRight
{
    [GrainType("collision-right-probe")]
    public sealed class Probe : DigitalBrain.Neuron, DigitalBrain.INeuron<DigitalBrain.Core.Tests.PlanDay>
    {
        public Task HandleAsync(DigitalBrain.Core.Tests.PlanDay fact, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
