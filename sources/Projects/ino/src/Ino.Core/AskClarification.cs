namespace Ino.Core;

/// <summary>
/// Kernel-level conversational primitive — a neuron's response payload
/// that says "I need value X to continue, here are suggested values".
/// Pairs with <see cref="ProvideClarification"/> as the answer turn.
///
/// Implements <see cref="IHasRfwPayload"/> so the gateway can stream a
/// pre-rendered chip-row UI directly to the Flutter client without the
/// gateway having any knowledge of the asking domain. The client renders
/// the chips; tapping one fires <see cref="ProvideClarification"/> back
/// with the same correlation_id so the same neuron activation receives it.
/// </summary>
[GenerateSerializer]
public sealed record AskClarification(
    [property: Id(0)] string Field,
    [property: Id(1)] string Prompt,
    [property: Id(2)] string[] Suggestions,
    [property: Id(3)] byte[] RfwDescriptionBytes,
    [property: Id(4)] byte[] RfwDataBytes) : ISynapse, IHasRfwPayload
{
    public byte[] RfwDescription => RfwDescriptionBytes;
    public byte[] RfwData => RfwDataBytes;
    public string ContentType => "ask_clarification";
}
