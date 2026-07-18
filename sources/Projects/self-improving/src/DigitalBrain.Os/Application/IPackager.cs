using DigitalBrain.Protocol;
using DigitalBrain.Protocol.Domain.Events;
using DigitalBrain.Protocol.Domain.ValueObjects.Distribution;

namespace DigitalBrain.Os.Application;

public interface IPackager : INeuron, IHandle<PackExperience>
{
    // Extended for contract support (trailing optionals keep all prior call sites compiling unchanged).
    Task<ExperiencePacked> PackAsync(string experienceId, string? description = null, string version = "0.1.0", string? inoContent = null, bool isContractOnly = false, ContractDeclaration[]? contractHandlers = null, CancellationToken cancellationToken = default);

    // Contract-only pack (private path): produces .brain with manifest + contract.json (decls), no impl/.ino.
    // Decls mirror the KnownContracts shape from sourcegen; enables subscriber growth + dispatch on receivers that supply local impls.
    Task<ExperiencePacked> PackContractAsync(string contractId, string? description = null, string version = "0.1.0", ContractDeclaration[]? declarations = null, CancellationToken cancellationToken = default);
}
