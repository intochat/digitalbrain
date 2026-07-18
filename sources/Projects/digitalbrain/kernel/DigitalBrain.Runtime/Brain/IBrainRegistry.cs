namespace DigitalBrain.Runtime.Brain;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Orleans;

[GenerateSerializer]
public sealed record BrainMetadata(
    [property: Id(0)] string BrainId,
    [property: Id(1)] string Name,
    [property: Id(2)] DateTimeOffset CreatedAt);

[Orleans.Metadata.DefaultGrainType("BrainRegistryGrain")]
public interface IBrainRegistry : IGrainWithGuidKey
{
    Task<BrainId> CreateBrainAsync(string name, string? seedTemplate = null);
    Task DeleteBrainAsync(string brainId);
    Task RenameBrainAsync(string brainId, string newName);
    Task<IReadOnlyList<BrainMetadata>> ListBrainsAsync();
    Task<BrainMetadata?> GetBrainAsync(string brainId);
}
