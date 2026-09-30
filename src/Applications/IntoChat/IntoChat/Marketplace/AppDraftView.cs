using DigitalBrain.Apps;

namespace IntoChat.Marketplace;

[GenerateSerializer, Alias("intochat.app-draft-view")]
public sealed record AppDraftView(
    [property: Id(0)] AppDraftState Draft,
    [property: Id(1)] AppVerification? Verification);
