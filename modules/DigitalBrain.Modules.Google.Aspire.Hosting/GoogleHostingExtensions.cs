using System.Runtime.CompilerServices;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Aspire.Hosting;

namespace DigitalBrain.Google.Aspire.Hosting;

public static class GoogleHostingExtensions
{
    private static readonly ConditionalWeakTable<GoogleModule, GoogleHostingState> States = new();

    public static GoogleModule WithGmail(this GoogleModule module)
    {
        ArgumentNullException.ThrowIfNull(module);

        States.GetValue(module, CreateState).AddGmail();
        return module;
    }

    private static GoogleHostingState CreateState(GoogleModule module)
    {
        var brain = BrainModuleHosting.BrainOf(module);
        var state = new GoogleHostingState(brain);

        BrainModuleHosting.AddReference(brain, state);
        return state;
    }

    private sealed class GoogleHostingState(BrainService brain) : BrainModuleReference
    {
        private bool _gmail;
        private IResourceBuilder<ParameterResource>? _clientId;
        private IResourceBuilder<ParameterResource>? _clientSecret;
        private IResourceBuilder<ParameterResource>? _redirectUri;

        internal void AddGmail()
        {
            if (_gmail)
            {
                throw new InvalidOperationException(
                    $"Gmail is already configured on brain '{brain.Name}'. Add it exactly once.");
            }

            _gmail = true;
            _clientId = brain.Builder
                .AddParameter("google-client-id")
                .WithDescription(
                    "OAuth client ID from [Google Auth Platform](https://console.cloud.google.com/auth/clients).",
                    enableMarkdown: true);
            _clientSecret = brain.Builder
                .AddParameter("google-client-secret", secret: true)
                .WithDescription(
                    "OAuth client secret from [Google Auth Platform](https://console.cloud.google.com/auth/clients).",
                    enableMarkdown: true);
            _redirectUri = brain.Builder
                .AddParameter("google-redirect-uri")
                .WithDescription(
                    "HTTP loopback callback URI registered on the Google OAuth client, for example `http://localhost:41001/callback`.",
                    enableMarkdown: true);
        }

        public override void Apply<T>(IResourceBuilder<T> builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            if (!_gmail)
            {
                return;
            }

            builder
                .WithEnvironment("DigitalBrain__Google__Gmail__ClientId", _clientId!)
                .WithEnvironment("DigitalBrain__Google__Gmail__ClientSecret", _clientSecret!)
                .WithEnvironment("DigitalBrain__Google__Gmail__RedirectUri", _redirectUri!);
        }
    }
}
