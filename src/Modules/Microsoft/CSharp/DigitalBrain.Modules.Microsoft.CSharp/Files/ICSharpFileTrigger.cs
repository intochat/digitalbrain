using DigitalBrain.Contracts;
using Orleans.Concurrency;
using Orleans.Metadata;

namespace DigitalBrain.Microsoft.CSharp;

// One-way so the publishing neuron never waits while a triggered run starts (possibly the sandbox too).
[Alias("microsoft.csharp.file-trigger"), DefaultGrainType("microsoft.csharp.file")]
internal interface ICSharpFileTrigger : IGrainWithStringKey
{
    [OneWay] Task Fire(Signal signal);
}
