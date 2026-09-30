using DigitalBrain.Core;
using Orleans;

namespace DigitalBrain.Microsoft.DotNet;

[GrainType("microsoft.dotnet")]
internal sealed class DotnetNeuron(DotnetRunner runner) : Neuron, IDotnet
{
    public Task<BuildOutcome> Build(BuildRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return runner.BuildAsync(request.Path, request.ArtifactsPath, cancellationToken);
    }

    public Task<TestOutcome> Test(TestRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return runner.TestAsync(request.Path, request.FilterClass, request.ArtifactsPath, cancellationToken);
    }
}
