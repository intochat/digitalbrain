using Xunit;

// Orleans in-process cluster boots are CPU-heavy (real in-memory silos, not mocks). xUnit's unqualified default
// (MaxParallelThreads = Environment.ProcessorCount) oversubscribes a many-core dev box badly enough that
// cross-silo/3-replica tests can blow through Orleans's 30s response timeout under contention.
// CollectionBehavior's argument must be a compile-time constant, so this can't scale off the actual core
// count — pinned to 2 to match GitHub's standard-tier ubuntu-latest runner (the CI target this suite is
// tuned for) exactly, rather than picking a number that would raise CI's concurrency above what it already
// runs at today.
[assembly: CollectionBehavior(MaxParallelThreads = 2)]
