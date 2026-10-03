using DigitalBrain;
using DigitalBrain.Kernel;
using DigitalBrain.Microsoft.CSharp;
using Orleans.Runtime;

namespace DigitalBrain.Modules.Microsoft.CSharp.Tests.Unit;

public interface IAppBindingTestHost : INeuron
{
    Task Bind(string file, string? overrideApp = null);
    Task Start(string file);
}

[GrainType("apps.app")]
public sealed class AppBindingTestHost : Neuron, IAppBindingTestHost
{
    public override DigitalBrain.Kernel.Enforcement.NeuronAccess Access(string operation)
        => DigitalBrain.Kernel.Enforcement.NeuronAccess.PublicOperation;
    public Task Bind(string file, string? overrideApp = null)
        => GrainFactory.GetGrain<ICSharpAppBinding>(file).BindApp(overrideApp ?? this.GetPrimaryKeyString());
    public async Task Start(string file)
    {
        var behavior = GrainFactory.GetGrain<ICSharpFile>(file);
        await behavior.Write("Console.WriteLine(1);");
        await behavior.Start();
    }
}
