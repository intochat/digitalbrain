namespace DigitalBrain.Compute.Storage;

internal interface IComputePart : IGrainWithStringKey
{
    Task Put(string json);
    Task<string> Read();
}
