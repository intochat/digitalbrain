using DigitalBrain.Contracts;

namespace DigitalBrain.Coding;

[GenerateSerializer, Alias("coding.draft-saved")]
public sealed record CodeDraftSaved([property: Id(0)] string DraftId, [property: Id(1)] long Revision) : Signal;

[GenerateSerializer, Alias("coding.check-changed")]
public sealed record CodeCheckChanged(
    [property: Id(0)] string DraftId,
    [property: Id(1)] Guid OperationId,
    [property: Id(2)] long Revision,
    [property: Id(3)] CodeCheckStatus Status) : Signal;
