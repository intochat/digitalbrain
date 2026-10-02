using DigitalBrain.Contracts;

namespace DigitalBrain.Postgres;

[PlatformOnly]
public interface IPostgresTables : INeuron
{
    [Orleans.Concurrency.AlwaysInterleave]
    Task Register(string tableId);
    [Orleans.Concurrency.AlwaysInterleave]
    Task RequireOpen();
    Task Retire();
}

[PlatformOnly]
public interface IPostgresTableLifetime : INeuron
{
    Task Retire(string owner);
}

[PlatformOnly]
public interface IPostgresStorageMigration : INeuron
{
    Task Ensure();
}
