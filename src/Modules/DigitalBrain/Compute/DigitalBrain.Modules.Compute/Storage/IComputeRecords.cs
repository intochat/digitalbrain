using DigitalBrain.Contracts;

namespace DigitalBrain.Compute.Storage;

internal interface IComputeRecords : INeuron
{
    Task<int> Put(ComputeStoredRecord[] records, bool overwrite);
    Task<ComputeStoredRecord[]> Read();
    Task<ComputeRecordPage> Page(string? before, int limit);
}
