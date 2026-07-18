using DigitalBrain.Protocol;
using DigitalBrain.Os;
using DigitalBrain.Os.Application;
using DigitalBrain.Protocol.Domain.Events;
using DigitalBrain.Os.Domain.Events;
using DigitalBrain.Os.Infrastructure.Orleans;
using DigitalBrain.Os.UI;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Cryptography;
using System.Text;

namespace DigitalBrain.Sdk.Experiences;

using Orleans;

// T2 polyrepo: extracted from Kernel/Experiences to Connectors per vision §4 (connectors = fs/google/http/gmail extracted; "Google is not special-cased").
// GrainType + IHandle + all logic/GrainFactory/Emit/journals 100% identical for no behavior change. Seeds os/google-auth.ino + pa + bundle ids untouched (read-only).
// Self-exp name: GoogleAuthConnectorNeuron (class); GrainType("google-auth") preserved exactly for activation compat with launcher name maps, INeuron(key), tests, distribution, .ino triggers.
[GrainType("google-auth")]
public sealed class GoogleAuthConnectorNeuron : Neuron,
    IHandle<BeginGoogleAuth>,
    IHandle<GoogleAuthCompleted>,
    IHandle<CapabilityDecision>,
    IHandle<CapabilityGrantRequest>
{
    private readonly IServiceProvider _services;
    private readonly HashSet<string> _allowed = new(StringComparer.OrdinalIgnoreCase);
    private string? _pendingCodeVerifier;

    public GoogleAuthConnectorNeuron(IServiceProvider services)
    {
        _services = services;
    }

    public Task HandleAsync(BeginGoogleAuth request, CancellationToken cancellationToken)
    {
        // PKCE per RFC 7636: generate verifier (server side secret), derive challenge, include challenge+S256 in emitted auth link (simulate carries semantics for test; real for Google).
        // Verifier held in grain (per brain key) for retrieval by Program callback on /oauth/callback (same key), cleared after use.
        // State in URL is brain key for routing Completed back to correct grain.
        var codeVerifier = GenerateCodeVerifier();
        var pkceCodeChallenge = CreateCodeChallenge(codeVerifier);
        _pendingCodeVerifier = codeVerifier;
        var baseUrl = "http://127.0.0.1:8080";
        var redirectUri = baseUrl + "/oauth/callback";
        var simulate = $"{baseUrl}/oauth/simulate?state={Uri.EscapeDataString(Self.Key)}&code_challenge={Uri.EscapeDataString(pkceCodeChallenge)}&code_challenge_method=S256";
        var realTemplate = $"https://accounts.google.com/o/oauth2/v2/auth?client_id=demo&redirect_uri={Uri.EscapeDataString(redirectUri)}&response_type=code&scope=https://www.googleapis.com/auth/gmail.readonly&state={Uri.EscapeDataString(Self.Key)}&code_challenge={Uri.EscapeDataString(pkceCodeChallenge)}&code_challenge_method=S256";
        // For real Google (documented): register client_id (public, PKCE no client secret), add exact loopback redirect_uri http://127.0.0.1:8080/oauth/callback to OAuth consent screen + credentials in console.cloud.google.com; use non-demo client_id here.
        return Emit(new AuthLinkReady(simulate, "Connect Google (PKCE loopback)"));
    }

    public async Task HandleAsync(GoogleAuthCompleted completed, CancellationToken cancellationToken)
    {
        // Received StatusOrTokenHint is now the post-exchange access token (from Program callback PKCE /token, or direct test hint "ya29.*").
        await Emit(new GoogleAuthCompleted("stored"));
    }

    // GmailLastSendersRequest handling moved to dedicated GmailConnectorNeuron for separate capsule identity. GoogleAuth focuses on auth + its own grants.

    public Task HandleAsync(CapabilityDecision decision, CancellationToken cancellationToken)
    {
        if (string.Equals(decision.BundleId, "google-auth", StringComparison.OrdinalIgnoreCase) && decision.Allowed)
        {
            _allowed.Add("SaveFileRequest");
            _allowed.Add("GoogleApi");
        }
        return Task.CompletedTask;
    }

    public Task HandleAsync(CapabilityGrantRequest request, CancellationToken cancellationToken)
    {
        // Emit a grant UI surface with explicit Allow/Deny (rendered by clients; taps send Decision back to this neuron).
        // grant UI surface now produced by rule in google-auth.ino (show card triggered by this CapabilityGrantRequest event)
        return Task.CompletedTask;
    }

    // Demo token path removed (compat seam cleaned per review). Callers (tests/CI) fall back to demo senders or explicit PKCE verifier flow. Real durable per-(account,provider,scope) lives in CredentialVaultNeuron (Kernel).

    public string? GetAndClearPendingCodeVerifier()
    {
        var verifier = _pendingCodeVerifier;
        _pendingCodeVerifier = null;
        return verifier;
    }

    private static string GenerateCodeVerifier()
    {
        // RFC 7636 section 4.1: code_verifier is a high-entropy cryptographic random string (43-128 chars) using unreserved [A-Z a-z 0-9 - . _ ~].
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Base64UrlEncode(bytes);
    }

    private static string CreateCodeChallenge(string codeVerifier)
    {
        // RFC 7636 section 4.2: code_challenge = BASE64URL-ENCODE(SHA256(ASCII(code_verifier)))
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.ASCII.GetBytes(codeVerifier));
        return Base64UrlEncode(hash);
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        // RFC 4648 base64url encoding (no padding, use - and _ )
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }
}
