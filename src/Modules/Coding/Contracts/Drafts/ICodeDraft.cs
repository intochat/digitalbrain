using DigitalBrain.Contracts;
using Orleans.Metadata;

namespace DigitalBrain.Coding;

[Alias("coding.draft"), DefaultGrainType("coding.draft")]
public interface ICodeDraft : INeuron
{
    Task<CodeDraftSnapshot> Read(CancellationToken cancellationToken = default);
    Task<CodeDraftSnapshot> Save(SaveCodeDraft request, CancellationToken cancellationToken = default);
    Task<CodeCheckSnapshot> Check(CheckCodeDraft request, CancellationToken cancellationToken = default);
    Task<CodeCheckSnapshot> ReadCheck(Guid operationId, CancellationToken cancellationToken = default);
    Task CancelCheck(Guid operationId, CancellationToken cancellationToken = default);
}
