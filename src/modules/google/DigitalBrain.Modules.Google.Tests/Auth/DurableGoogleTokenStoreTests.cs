using System.Text;
using DigitalBrain.Google.Auth;
using DigitalBrain.Security;
using Google.Apis.Auth.OAuth2.Responses;
using Orleans.Journaling;
using Xunit;

namespace DigitalBrain.Google.Tests.Auth;

public sealed class DurableGoogleTokenStoreTests
{
    private const string AccessToken = "ya29.access-token-secret-value";
    private const string RefreshToken = "1//refresh-token-secret-value";

    [Fact(DisplayName = "Purpose string is google/oauth/{connection}/{durableIdentity}")]
    public void Purpose_uses_google_oauth_scheme()
    {
        Assert.Equal(
            "google/oauth/reader@example.com/owner/gmail/reader",
            DurableGoogleTokenStore.Purpose("reader@example.com", "owner/gmail/reader"));
    }

    [Fact(DisplayName = "StoreAsync protects TokenResponse so durable bytes never contain raw tokens")]
    public async Task StoreAsync_protects_token_bytes_at_rest()
    {
        var state = new TestDurableValue<byte[]>([]);
        var protector = new ScramblingProtector();
        var store = new DurableGoogleTokenStore(
            state,
            static () => ValueTask.CompletedTask,
            protector,
            DurableGoogleTokenStore.Purpose("gmail", "id-1"));

        await store.StoreAsync(
            "user-1",
            new TokenResponse
            {
                AccessToken = AccessToken,
                RefreshToken = RefreshToken,
                ExpiresInSeconds = 3600,
                TokenType = "Bearer",
            });

        Assert.NotNull(state.Value);
        Assert.NotEmpty(state.Value);
        var durableText = Encoding.UTF8.GetString(state.Value);
        Assert.DoesNotContain(AccessToken, durableText, StringComparison.Ordinal);
        Assert.DoesNotContain(RefreshToken, durableText, StringComparison.Ordinal);
        Assert.Equal(DurableGoogleTokenStore.Purpose("gmail", "id-1"), protector.LastPurpose);

        var loaded = await store.GetAsync<TokenResponse>("user-1");
        Assert.NotNull(loaded);
        Assert.Equal(AccessToken, loaded.AccessToken);
        Assert.Equal(RefreshToken, loaded.RefreshToken);
    }

    [Fact(DisplayName = "StoreAsync rolls back durable state when commit fails")]
    public async Task StoreAsync_rolls_back_when_commit_fails()
    {
        var state = new TestDurableValue<byte[]>([]);
        var commits = 0;
        var store = new DurableGoogleTokenStore(
            state,
            () =>
            {
                commits++;
                if (commits > 1)
                {
                    return ValueTask.FromException(new InvalidOperationException("commit failed"));
                }

                return ValueTask.CompletedTask;
            },
            new ScramblingProtector(),
            DurableGoogleTokenStore.Purpose("gmail", "id-1"));

        await store.StoreAsync(
            "user-1",
            new TokenResponse { AccessToken = AccessToken, RefreshToken = RefreshToken });
        var prior = state.Value;
        Assert.NotNull(prior);
        Assert.NotEmpty(prior);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.StoreAsync(
            "user-1",
            new TokenResponse { AccessToken = "replacement-access", RefreshToken = "replacement-refresh" }));

        Assert.Same(prior, state.Value);
        var loaded = await store.GetAsync<TokenResponse>("user-1");
        Assert.NotNull(loaded);
        Assert.Equal(AccessToken, loaded.AccessToken);
        Assert.Equal(RefreshToken, loaded.RefreshToken);
    }

    [Fact(DisplayName = "DeleteAsync and ClearAsync remove protected entries with commit rollback")]
    public async Task Delete_and_clear_round_trip()
    {
        var state = new TestDurableValue<byte[]>([]);
        var store = new DurableGoogleTokenStore(
            state,
            static () => ValueTask.CompletedTask,
            new ScramblingProtector(),
            DurableGoogleTokenStore.Purpose("gmail", "id-1"));

        await store.StoreAsync("user-1", new TokenResponse { AccessToken = AccessToken, RefreshToken = RefreshToken });
        await store.StoreAsync("user-2", new TokenResponse { AccessToken = "other-access", RefreshToken = "other-refresh" });

        await store.DeleteAsync<TokenResponse>("user-1");
        Assert.Null(await store.GetAsync<TokenResponse>("user-1"));
        Assert.NotNull(await store.GetAsync<TokenResponse>("user-2"));

        await store.ClearAsync();
        Assert.Null(await store.GetAsync<TokenResponse>("user-2"));
        Assert.True(state.Value is not { Length: > 0 });
    }

    private sealed class ScramblingProtector : IDurablePayloadProtector
    {
        public string LastPurpose { get; private set; } = "";

        public byte[] Protect(string purpose, ReadOnlySpan<byte> plaintext)
        {
            LastPurpose = purpose;
            var protectedPayload = new byte[plaintext.Length];
            for (var i = 0; i < plaintext.Length; i++)
            {
                protectedPayload[i] = (byte)(plaintext[i] ^ 0xA5);
            }

            return protectedPayload;
        }

        public byte[] Unprotect(string purpose, ReadOnlySpan<byte> protectedPayload)
        {
            LastPurpose = purpose;
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
