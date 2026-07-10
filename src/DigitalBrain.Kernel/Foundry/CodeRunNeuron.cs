using DigitalBrain.Core;

namespace DigitalBrain.Kernel.Foundry;

[GrainType("digitalbrain.coderun")]
public class CodeRunNeuron(ILogger<CodeRunNeuron> logger, NeuronJournals journals) : Neuron(logger, journals), ICodeRunNeuron
{
    public async Task HandleAsync(RunGeneratedCode cmd, CancellationToken cancellationToken = default)
    {
        var executor = ServiceProvider.GetRequiredService<ICodeExecutor>();
        var result = executor.Execute(cmd.Source, cmd.Entrypoint);
        await FireAsync(new CodeRunResult(result.Success, result.Output, result.Error), cancellationToken);
    }
}

