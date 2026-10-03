using DigitalBrain;
using DigitalBrain.Contracts;

namespace DigitalBrain.Apps;

[GenerateSerializer, Alias("intochat.app-draft-changed")]
public sealed record AppDraftChanged([property: Id(0)] string DraftId, [property: Id(1)] long Revision, [property: Id(2)] AppDraftStatus Status) : Signal;

