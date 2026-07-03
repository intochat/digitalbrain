using DigitalBrain.Core;
using DigitalBrain.Kernel.Ui;
using DigitalBrain.Salesforce;

namespace DigitalBrain.Kernel.Salesforce;

[GrainType("digitalbrain.salesforce.auth.v1")]
public class SalesforceAuthNeuron(ILogger<SalesforceAuthNeuron> logger, NeuronJournals journals)
    : Neuron(logger, journals), ISalesforceAuthNeuron
{
    public static AuthButtonSurface SignInSurface() => new(
        Provider: "salesforce",
        Label: "Connect Salesforce",
        Icon: "salesforce",
        Action: SalesforceSignals.AuthRequested);

    public async Task HandleAsync(Signal signal)
    {
        if (signal.Name != SalesforceSignals.AuthRequested)
            return;

        var sessionId = signal.Props.TryGetValue("sessionId", out var value) ? value?.ToString() : null;
        var surface = SalesforceAuthSurfaces.CredentialForm(Self.Value, sessionId);

        await FireAsync(surface);
        ServiceProvider.GetService<HomeFeedBus>()?.Broadcast(UiSurfaceRfwBridge.FromUiSurface(surface, Self.Value));
    }
}
