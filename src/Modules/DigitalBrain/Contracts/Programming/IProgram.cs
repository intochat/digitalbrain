using DigitalBrain.Abstractions.Neurons;
using Orleans.Concurrency;

namespace DigitalBrain.Abstractions.Programming;

[Alias("program")]
public interface IProgram : INeuron
{
    [ReadOnly, Alias("read")]
    Task<ProgramSnapshot> Read();
}

[Alias("program-run")]
public interface IProgramRun : INeuron
{
    [ReadOnly, Alias("read")]
    Task<ProgramRunSnapshot?> Read();
}

[Alias("program-catalog")]
public interface IProgramCatalog : INeuron
{
    [ReadOnly, Alias("list")]
    Task<IReadOnlyList<string>> List();
}
