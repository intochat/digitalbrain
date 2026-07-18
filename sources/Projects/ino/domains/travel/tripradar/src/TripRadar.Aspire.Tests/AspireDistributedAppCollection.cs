using Xunit;

namespace TripRadar.Aspire.Tests;

// Each test in this collection spins up its own AppHost with Postgres/Kafka/Redis
// containers, so running them in parallel exhausts local resources and produces
// spurious timeouts (notably Kafka `Local: Message timed out`). Serializing them
// via a shared collection keeps the suite deterministic.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AspireDistributedAppCollection
{
    public const string Name = "AspireDistributedApp";
}
