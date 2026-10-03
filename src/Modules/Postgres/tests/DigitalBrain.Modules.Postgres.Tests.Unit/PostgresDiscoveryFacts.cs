using DigitalBrain.Contracts;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Contracts.Signals;
using DigitalBrain.Kernel.Enforcement;
using DigitalBrain.Postgres;

namespace DigitalBrain.Modules.Postgres.Tests.Unit;

public sealed class PostgresDiscoveryFacts
{
    [Fact]
    public async Task AppIndexFailurePreservesPlatformToolsWithoutLeakingExceptionDetails()
    {
        CallerContextStamper.Stamp(new() { PrincipalId = "user", AccountId = "user", BrainId = "brain", Kind = CallerKind.Assistant, StampedBy = TrustedEdge.AuthenticatedHttp });
        try
        {
            var result = await new PostgresRegistryResources(new PostgresAppResources(new FailingBrain()), global::Microsoft.Extensions.Logging.Abstractions.NullLogger<PostgresRegistryResources>.Instance)
                .Discover(TestContext.Current.CancellationToken);
            Assert.Contains("postgres_schema", Assert.Single(result.Capabilities).Tools);
            var error = Assert.Single(result.Errors);
            Assert.Equal("postgres", error.Provider);
            Assert.DoesNotContain("private", error.Message, StringComparison.Ordinal);
        }
        finally { Orleans.Runtime.RequestContext.Remove(CallerContextStamper.RequestContextKey); }
    }

    private sealed class FailingBrain : IDigitalBrain
    {
        public T Get<T>(string id) where T : class, IGrainWithStringKey => throw new IOException("private database details");
        public Task<ISignalSubscription<T>> SubscribeAsync<T>(INeuron source, CancellationToken cancellationToken = default) where T : Signal => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
