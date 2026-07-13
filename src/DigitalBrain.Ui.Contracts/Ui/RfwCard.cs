namespace DigitalBrain.Ui.Contracts.Ui;

using DigitalBrain.Core;

// A Remote Flutter Widgets payload pushed from a neuron to the Home feed. DataJson is an opaque blob (RFW data
// is dynamic, so there is no static schema). This is the second server-driven-UI payload kind alongside UiSurface
// (the canonical SDUI model stays UiSurface; RfwCard is added for the streaming RFW feed). Harvested from digitalbrain.
[GenerateSerializer]
[Alias("DigitalBrain.Ui.Contracts.Ui.RfwCard")]
public record RfwCard(string LibraryName, string RootWidget, string DataJson, string? ClientId = null)
    : Synapse(nameof(RfwCard), DateTimeOffset.UtcNow);
