namespace IntoChat.Packages;

internal sealed record SynapseAccountOptions(string Revision, IReadOnlyList<SynapseAccountSlot> Slots);
internal sealed record SynapseAccountSlot(string Name, string Source, string Description, IReadOnlyList<SynapseAccountChoice> Accounts);
internal sealed record SynapseAccountChoice(string Id, string Label);
