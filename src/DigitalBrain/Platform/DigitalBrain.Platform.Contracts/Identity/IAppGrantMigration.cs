using DigitalBrain.Contracts;
using Orleans;

namespace DigitalBrain.Platform.Contracts.Identity;

[PlatformOnly]
public interface IAppGrantMigration : IGrainWithStringKey
{
    Task Migrate(string appId, string[] legacyFiles);
}
