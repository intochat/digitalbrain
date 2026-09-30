using DigitalBrain.Core;

namespace DigitalBrain.Memory;

public sealed class MemoryModuleOptions : IModuleOptions
{
    public string? CollectionName { get; set; }

    public void Validate()
    {
        if (CollectionName is { } name && string.IsNullOrWhiteSpace(name))
        { throw new ArgumentException("The memory collection name must not be blank when it is set."); }
    }
}
