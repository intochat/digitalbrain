namespace DigitalBrain.Assistant;

internal sealed record PackageAccountOptions(string Revision, IReadOnlyList<PackageAccountSlot> Slots);
internal sealed record PackageAccountSlot(string Name, string Source, string Description, IReadOnlyList<PackageAccountChoice> Accounts);
internal sealed record PackageAccountChoice(string Id, string Label);
