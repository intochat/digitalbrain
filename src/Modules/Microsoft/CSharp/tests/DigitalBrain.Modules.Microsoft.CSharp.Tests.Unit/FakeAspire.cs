using DigitalBrain.Kernel;
using DigitalBrain.Microsoft.Aspire;
using DigitalBrain.Microsoft.CSharp;
using Orleans.Runtime;

namespace DigitalBrain.Modules.Microsoft.CSharp.Tests.Unit;

public interface IFakeAspireProbe : IGrainWithStringKey
{
    Task<int> Starts();
}

// The AppHost as the runner sees it: the sandbox starts stopped and reports Running once asked to start.
[GrainType("microsoft.aspire")]
public sealed class FakeAspire : Neuron, IAspire, IFakeAspireProbe
{
    private AspireResource _sandbox = new(CSharpSandbox.ResourceName, "Container", "NotStarted", null, []);

    private int _starts;

    public Task<int> Starts() => Task.FromResult(_starts);

    public Task<IReadOnlyList<AspireResource>> ListResources(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<AspireResource>>([_sandbox]);

    public async Task StartResource(string name, CancellationToken cancellationToken = default)
    {
        _starts++;
        _sandbox = _sandbox with { State = "Running", Health = "Healthy", Urls = [FakeSandbox.Url] };
        await PublishAsync(new ResourceStateChanged(name, "Running", "Healthy", "NotStarted"));
    }

    public Task StopResource(string name, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task RestartResource(string name, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
