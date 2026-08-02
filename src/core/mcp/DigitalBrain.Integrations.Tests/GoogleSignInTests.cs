using System.Net;
using System.Security.Cryptography;
using System.Text;
using DigitalBrain.Google.Auth;
using DigitalBrain.Security;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Util;
using Orleans.Journaling;
using Xunit;

namespace DigitalBrain.Integrations.Tests;

public sealed class GoogleSignInTests
{
    private const string ClientId = "test-client.apps.googleusercontent.com";
    private const string ClientSecret = "test-client-secret";
    private const string RedirectUri = "https://ui.example/oauth/callback";
    private const string Scope = "https://www.googleapis.com/auth/gmail.readonly";
    private const string AccessToken = "ya29.fresh-access-token-value";
    private const string RefreshToken = "1//fresh-refresh-token-value";
    private const string RotatedAccessToken = "ya29.rotated-access-token-value";

    [Fact(DisplayName = "BuildAuthorizeUrl uses GoogleAuthorizationCodeFlow with offline access and state")]
    public void BuildAuthorizeUrlIncludesOfflineStateScopesAndClient()
    {
        var url = GoogleSignIn.BuildAuthorizeUrl(
            ClientId,
            RedirectUri,
            [Scope],
            state: "nonce-state-1");

        Assert.Equal("https", url.Scheme);
        Assert.Contains("accounts.google.com", url.Host, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("access_type=offline", url.Query, StringComparison.Ordinal);
        Assert.Contains($"client_id={Uri.EscapeDataString(ClientId)}", url.Query, StringComparison.Ordinal);
        Assert.Contains($"redirect_uri={Uri.EscapeDataString(RedirectUri)}", url.Query, StringComparison.Ordinal);
        Assert.Contains($"state={Uri.EscapeDataString("nonce-state-1")}", url.Query, StringComparison.Ordinal);
        Assert.Contains(Uri.EscapeDataString(Scope), url.Query, StringComparison.Ordinal);
        Assert.Contains("response_type=code", url.Query, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "ExchangeAsync stores TokenResponse into IDataStore")]
    public async Task ExchangeAsyncStoresTokenInDataStore()
    {
        await using var tokenHost = await FakeGoogleTokenHost.StartAsync();
        var state = new TestDurableValue<byte[]>([]);
        var store = new DurableGoogleTokenStore(
            state,
            static () => ValueTask.CompletedTask,
            new ScramblingProtector(),
            DurableGoogleTokenStore.Purpose("gmail", "user-id"));
        tokenHost.ExchangeResponse = SuccessToken(AccessToken, RefreshToken, expiresIn: 3600);

        await using var signIn = GoogleSignIn.Create(
            ClientId,
            ClientSecret,
            [Scope],
            store,
            tokenHost.TokenServerUrl);

        var token = await signIn.ExchangeAsync("user-1", "auth-code-1", RedirectUri, TestContext.Current.CancellationToken);

        Assert.Equal(AccessToken, token.AccessToken);
        Assert.Equal(RefreshToken, token.RefreshToken);
        var stored = await store.GetAsync<TokenResponse>("user-1");
        Assert.NotNull(stored);
        Assert.Equal(AccessToken, stored.AccessToken);
        Assert.Equal(RefreshToken, stored.RefreshToken);
        var durableText = Encoding.UTF8.GetString(state.Value);
        Assert.DoesNotContain(AccessToken, durableText, StringComparison.Ordinal);
        Assert.DoesNotContain(RefreshToken, durableText, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Stale TokenResponse auto-refreshes on use and re-stores")]
    public async Task StaleTokenAutoRefreshesOnUse()
    {
        await using var tokenHost = await FakeGoogleTokenHost.StartAsync();
        var state = new TestDurableValue<byte[]>([]);
        var store = new DurableGoogleTokenStore(
            state,
            static () => ValueTask.CompletedTask,
            new ScramblingProtector(),
            DurableGoogleTokenStore.Purpose("gmail", "user-id"));
        var clock = new FixedClock(new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc));
        tokenHost.RefreshResponse = SuccessToken(RotatedAccessToken, refreshToken: null, expiresIn: 3600);

        await store.StoreAsync(
            "user-1",
            new TokenResponse
            {
                AccessToken = AccessToken,
                RefreshToken = RefreshToken,
                ExpiresInSeconds = 60,
                TokenType = "Bearer",
                IssuedUtc = clock.UtcNow.AddHours(-2),
            });

        await using var signIn = GoogleSignIn.Create(
            ClientId,
            ClientSecret,
            [Scope],
            store,
            tokenHost.TokenServerUrl,
            clock);

        using var service = await signIn.CreateServiceAsync("user-1", TestContext.Current.CancellationToken);
        Assert.NotNull(service);

        var credential = Assert.IsType<global::Google.Apis.Auth.OAuth2.UserCredential>(service.HttpClientInitializer);
        var access = await credential.GetAccessTokenForRequestAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(RotatedAccessToken, access);

        var stored = await store.GetAsync<TokenResponse>("user-1");
        Assert.NotNull(stored);
        Assert.Equal(RotatedAccessToken, stored.AccessToken);
        Assert.Equal(RefreshToken, stored.RefreshToken);
        Assert.Equal(1, tokenHost.RefreshCount);
    }

    [Fact(DisplayName = "Refresh response without refresh_token preserves the old refresh token in the store")]
    public async Task RefreshWithoutRefreshTokenPreservesOld()
    {
        await using var tokenHost = await FakeGoogleTokenHost.StartAsync();
        var store = new DurableGoogleTokenStore(
            new TestDurableValue<byte[]>([]),
            static () => ValueTask.CompletedTask,
            new ScramblingProtector(),
            DurableGoogleTokenStore.Purpose("gmail", "user-id"));
        var clock = new FixedClock(new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc));
        tokenHost.RefreshResponse = SuccessToken(RotatedAccessToken, refreshToken: null, expiresIn: 3600);

        await store.StoreAsync(
            "user-1",
            new TokenResponse
            {
                AccessToken = AccessToken,
                RefreshToken = RefreshToken,
                ExpiresInSeconds = 60,
                TokenType = "Bearer",
                IssuedUtc = clock.UtcNow.AddHours(-2),
            });

        await using var signIn = GoogleSignIn.Create(
            ClientId,
            ClientSecret,
            [Scope],
            store,
            tokenHost.TokenServerUrl,
            clock);

        using var service = await signIn.CreateServiceAsync("user-1", TestContext.Current.CancellationToken);
        var credential = Assert.IsType<global::Google.Apis.Auth.OAuth2.UserCredential>(service.HttpClientInitializer);
        await credential.GetAccessTokenForRequestAsync(cancellationToken: TestContext.Current.CancellationToken);

        var stored = await store.GetAsync<TokenResponse>("user-1");
        Assert.NotNull(stored);
        Assert.Equal(RefreshToken, stored.RefreshToken);
        Assert.Equal(RotatedAccessToken, stored.AccessToken);
    }

    [Fact(DisplayName = "Token error response surfaces as TokenResponseException and stores nothing")]
    public async Task ErrorResponseSurfacesTypedFailureAndStoresNothing()
    {
        await using var tokenHost = await FakeGoogleTokenHost.StartAsync();
        var state = new TestDurableValue<byte[]>([]);
        var store = new DurableGoogleTokenStore(
            state,
            static () => ValueTask.CompletedTask,
            new ScramblingProtector(),
            DurableGoogleTokenStore.Purpose("gmail", "user-id"));
        tokenHost.ExchangeStatusCode = HttpStatusCode.BadRequest;
        tokenHost.ExchangeError = new { error = "invalid_grant", error_description = "Bad code" };

        await using var signIn = GoogleSignIn.Create(
            ClientId,
            ClientSecret,
            [Scope],
            store,
            tokenHost.TokenServerUrl);

        var failure = await Assert.ThrowsAsync<TokenResponseException>(
            () => signIn.ExchangeAsync("user-1", "bad-code", RedirectUri, TestContext.Current.CancellationToken));

        Assert.Equal("invalid_grant", failure.Error.Error);
        Assert.Null(await store.GetAsync<TokenResponse>("user-1"));
        Assert.True(state.Value is not { Length: > 0 });
    }

    [Fact(DisplayName = "CreateServiceAsync builds GmailService with ApplicationName DigitalBrain")]
    public async Task CreateServiceAsyncBuildsGmailService()
    {
        var store = new DurableGoogleTokenStore(
            new TestDurableValue<byte[]>([]),
            static () => ValueTask.CompletedTask,
            new ScramblingProtector(),
            DurableGoogleTokenStore.Purpose("gmail", "user-id"));
        await store.StoreAsync(
            "user-1",
            new TokenResponse
            {
                AccessToken = AccessToken,
                RefreshToken = RefreshToken,
                ExpiresInSeconds = 3600,
                TokenType = "Bearer",
                IssuedUtc = DateTime.UtcNow,
            });

        await using var signIn = GoogleSignIn.Create(ClientId, ClientSecret, [Scope], store);
        using var service = await signIn.CreateServiceAsync("user-1", TestContext.Current.CancellationToken);

        Assert.Equal("DigitalBrain", service.ApplicationName);
        Assert.IsType<global::Google.Apis.Auth.OAuth2.UserCredential>(service.HttpClientInitializer);
    }

    private static object SuccessToken(string accessToken, string? refreshToken, int expiresIn)
    {
        if (refreshToken is null)
        {
            return new
            {
                access_token = accessToken,
                token_type = "Bearer",
                expires_in = expiresIn,
            };
        }

        return new
        {
            access_token = accessToken,
            refresh_token = refreshToken,
            token_type = "Bearer",
            expires_in = expiresIn,
        };
    }

    private sealed class FixedClock(DateTime utcNow) : IClock
    {
        public DateTime Now => utcNow.ToLocalTime();
        public DateTime UtcNow => utcNow;
    }

    private sealed class ScramblingProtector : IDurablePayloadProtector
    {
        public byte[] Protect(string purpose, ReadOnlySpan<byte> plaintext)
        {
            _ = purpose;
            var protectedPayload = new byte[plaintext.Length];
            for (var i = 0; i < plaintext.Length; i++)
            {
                protectedPayload[i] = (byte)(plaintext[i] ^ 0xA5);
            }

            return protectedPayload;
        }

        public byte[] Unprotect(string purpose, ReadOnlySpan<byte> protectedPayload)
        {
            _ = purpose;
            var plaintext = new byte[protectedPayload.Length];
            for (var i = 0; i < protectedPayload.Length; i++)
            {
                plaintext[i] = (byte)(protectedPayload[i] ^ 0xA5);
            }

            return plaintext;
        }
    }

    private sealed class TestDurableValue<T>(T value) : IDurableValue<T>
    {
        [System.Diagnostics.CodeAnalysis.AllowNull]
        public T Value { get; set; } = value;
    }
}
