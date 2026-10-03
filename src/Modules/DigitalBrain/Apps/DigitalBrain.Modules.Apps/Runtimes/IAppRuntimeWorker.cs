using DigitalBrain;
using DigitalBrain.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace DigitalBrain.Apps;

// Answers run here, off the app neuron, so a slow runtime such as a group chat never blocks the app's
// other calls. The result comes back through IApp.Respond exactly like a script's answer.
[Alias("apps.runtime-worker"), Orleans.Metadata.DefaultGrainType("apps.runtime-worker")]
public interface IAppRuntimeWorker : IGrainWithIntegerKey
{
    [OneWay] Task Answer(string appKey, PackageRevisionRef revision, AppInvocation invocation);
}
