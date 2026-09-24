using DigitalBrain.Contracts;
using Orleans.Metadata;

namespace DigitalBrain.Apps;

// One package revision installed in one workspace. Its behavior runs out of process and cannot host
// neurons, so the app is the behavior's address in the brain: settings, operations and invocations.
[Alias("apps.app"), DefaultGrainType("apps.app")]
public interface IApp : INeuron
{
    Task<AppSnapshot> Read();
    Task<AppSnapshot> Install(InstallApp request);
    Task<AppSnapshot> Configure(ConfigureApp request);
    Task<AppSnapshot> Upgrade(UpgradeApp request);
    Task<AppSnapshot> Uninstall(UninstallApp request);
    Task<AppInvocation> Invoke(InvokeApp request);
    Task<AppInvocation> Respond(AppResponse response);
    Task<AppInvocation> ReadInvocation(Guid invocationId);
    Task<IReadOnlyList<AppInvocation>> Pending();
}
