using DigitalBrain.Core;
using DigitalBrain.Google;

namespace DigitalBrain.Kernel.Google;

[GrainType("digitalbrain.google.auth.v1")]
public class GoogleAuthNeuron(ILogger<GoogleAuthNeuron> logger, NeuronJournals journals)
    : Neuron(logger, journals), IGoogleAuthNeuron
{
    // Dev placeholders - in real use from IConfiguration or PackConfig (client id/secret from Google Cloud Console)
    private const string DevClientId = "your-client-id.apps.googleusercontent.com";
    private const string DevClientSecret = "your-client-secret";
    private const string DevRedirectUri = "http://localhost:8080/google-callback"; // backend callback (to be wired)

    public static AuthButtonSurface SignInSurface() => new(
        Provider: "google",
        Label: "Sign in with Google",
        Icon: "google",
        Action: GoogleSignals.AuthRequested);

    public async Task HandleAsync(Signal signal)
    {
        if (signal.Name != GoogleSignals.AuthRequested) return;

        var url = GoogleCredentialFactory.CreateAuthorizationUrl(
            DevClientId,
            DevClientSecret,
            DevRedirectUri,
            "https://www.googleapis.com/auth/gmail.readonly",
            "https://www.googleapis.com/auth/gmail.modify");

        await FireAsync(new Signal(GoogleSignals.AuthUrl, new Dictionary<string, object?> { ["url"] = url }));
    }
}
