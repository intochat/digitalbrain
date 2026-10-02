namespace DigitalBrain.Apps;

internal sealed record AppAccountOptions(string Revision, IReadOnlyList<AppAccountSlot> Slots);
internal sealed record AppAccountSlot(string Name, string Source, string Description, IReadOnlyList<AppAccountChoice> Accounts);
internal sealed record AppAccountChoice(string Id, string Label);
