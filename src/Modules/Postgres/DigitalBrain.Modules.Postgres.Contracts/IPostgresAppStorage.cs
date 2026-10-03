using DigitalBrain;
using DigitalBrain.Contracts;

namespace DigitalBrain.Postgres;

[PlatformOnly]
public interface IPostgresAppStorage : INeuron
{
    Task Migrate(string[] legacyFiles);
    [Orleans.Concurrency.ReadOnly] Task<string[]> ReadTables();
}
