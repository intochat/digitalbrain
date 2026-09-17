using DigitalBrain.Abstractions.Descriptors;
using DigitalBrain.Abstractions.Neurons;
using Orleans.Concurrency;

namespace DigitalBrain.Abstractions.Programming;

[Alias("program")]
public interface IProgram : INeuron
{
    [ReadOnly, Alias("read")]
    [NeuronTool(IsReadOnly = true)]
    Task<ProgramSnapshot> Read();
}

[Alias("program-run")]
public interface IProgramRun : INeuron
{
    [ReadOnly, Alias("read")]
    [NeuronTool(IsReadOnly = true)]
    Task<ProgramRunSnapshot?> Read();
}

[Alias("program-catalog")]
public interface IProgramCatalog : INeuron
{
    [ReadOnly, Alias("list")]
    [NeuronTool(IsReadOnly = true)]
    Task<IReadOnlyList<string>> List();
}
