namespace DigitalBrain.Ui.Contracts.Ui;

[GenerateSerializer]
[Alias("DigitalBrain.Ui.Contracts.Ui.RfwCard")]
public record RfwCard(string LibraryName, string RootWidget, string DataJson, string? ClientId = null);
