using DigitalBrain.Contracts;
using Orleans.Metadata;

namespace DigitalBrain.Apps;

// One package revision installed in one workspace. Its script runs in a container and cannot host
// neurons, so the app is the script's address in the brain: settings, operations and invocations.
[Alias("apps.app"), DefaultGrainType("apps.app")]
public interface IApp : INeuron
{
    Task<AppSnapshot> Read();
    // Each lifecycle change stops one C# file and starts another, which can outlast the default call timeout.
    [ResponseTimeout("00:10:00")] Task<AppSnapshot> Install(InstallApp request);
    [ResponseTimeout("00:10:00")] Task<AppSnapshot> Configure(ConfigureApp request);
    [ResponseTimeout("00:10:00")] Task<AppSnapshot> Upgrade(UpgradeApp request);
    [ResponseTimeout("00:10:00")] Task<AppSnapshot> Uninstall(UninstallApp request);
    Task<AppInvocation> Invoke(InvokeApp request);
    Task<AppInvocation> Respond(AppResponse response);
    Task<AppInvocation> ReadInvocation(Guid invocationId);
    Task<IReadOnlyList<AppInvocation>> Pending();
}
