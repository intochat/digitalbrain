using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Security;
using DigitalBrain.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans.Journaling;
using Xunit;

namespace DigitalBrain.Behaviors.Tests;

public sealed class BehaviorProtectedPayloadBrokerEndpointsTests
{
    private static readonly OwnerId BoundOwner = new("broker-owner");
    private static readonly NeuronId BoundTask = NeuronId.For<ITask>(BoundOwner, "broker-task");
    private static readonly AttemptId BoundAttempt = new(Guid.Parse("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"));
    private static readonly byte[] Plaintext = "reverse-broker-secret"u8.ToArray();

    [Fact(DisplayName = "real reverse broker store/load handlers round-trip owner/task/attempt-bound payloads")]
    public async Task StoreLoadHandlersRoundTripBoundPayload()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await StartHostAsync(cancellationToken);
        using var client = CreateClient(host);

        using var store = await client.PostAsJsonAsync(
            "/v1/behaviors/broker/payloads/store",
            new
            {
                owner = BoundOwner.Value,
                taskType = BoundTask.Type,
                taskOwner = BoundTask.Owner.Value,
                taskName = BoundTask.Name,
                attempt = BoundAttempt.Value.ToString("N"),
                contentBase64 = Convert.ToBase64String(Plaintext),
            },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, store.StatusCode);
        var stored = await store.Content.ReadFromJsonAsync<ProtectedReferenceResponse>(
            cancellationToken: cancellationToken);
        Assert.NotNull(stored);
        Assert.False(string.IsNullOrWhiteSpace(stored.Id));
        Assert.NotNull(stored.ExpiresAt);

        using var load = await client.PostAsJsonAsync(
            "/v1/behaviors/broker/payloads/load",
            new
            {
                owner = BoundOwner.Value,
                taskType = BoundTask.Type,
                taskOwner = BoundTask.Owner.Value,
                taskName = BoundTask.Name,
                attempt = BoundAttempt.Value.ToString("N"),
                reference = new { id = stored.Id, expiresAt = stored.ExpiresAt },
            },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, load.StatusCode);
        var loaded = await load.Content.ReadFromJsonAsync<LoadPayloadResponse>(
            cancellationToken: cancellationToken);
        Assert.NotNull(loaded);
        Assert.Equal(Plaintext, Convert.FromBase64String(loaded.ContentBase64));
    }

    [Fact(DisplayName = "reverse broker rejects cross-owner, wrong task/attempt, empty content, and invalid base64")]
    public async Task StoreLoadHandlersRejectIdentityAndContentMisuse()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await StartHostAsync(cancellationToken);
        using var client = CreateClient(host);

        using var missingOwner = await client.PostAsJsonAsync(
            "/v1/behaviors/broker/payloads/store",
            new
            {
                taskType = BoundTask.Type,
                taskOwner = BoundTask.Owner.Value,
                taskName = BoundTask.Name,
                attempt = BoundAttempt.Value.ToString("N"),
                contentBase64 = Convert.ToBase64String(Plaintext),
            },
            cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, missingOwner.StatusCode);
        Assert.Equal("missing-owner", (await missingOwner.Content.ReadAsStringAsync(cancellationToken)).Trim());

        using var mismatch = await client.PostAsJsonAsync(
            "/v1/behaviors/broker/payloads/store",
            new
            {
                owner = BoundOwner.Value,
                taskType = BoundTask.Type,
                taskOwner = "foreign-owner",
                taskName = BoundTask.Name,
                attempt = BoundAttempt.Value.ToString("N"),
                contentBase64 = Convert.ToBase64String(Plaintext),
            },
            cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, mismatch.StatusCode);
        Assert.Equal("owner-task-mismatch", (await mismatch.Content.ReadAsStringAsync(cancellationToken)).Trim());

        using var empty = await client.PostAsJsonAsync(
            "/v1/behaviors/broker/payloads/store",
            new
            {
                owner = BoundOwner.Value,
                taskType = BoundTask.Type,
                taskOwner = BoundTask.Owner.Value,
                taskName = BoundTask.Name,
                attempt = BoundAttempt.Value.ToString("N"),
                contentBase64 = "",
            },
            cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal("empty-payload", (await empty.Content.ReadAsStringAsync(cancellationToken)).Trim());

        using var invalidBase64 = await client.PostAsJsonAsync(
            "/v1/behaviors/broker/payloads/store",
            new
            {
                owner = BoundOwner.Value,
                taskType = BoundTask.Type,
                taskOwner = BoundTask.Owner.Value,
                taskName = BoundTask.Name,
                attempt = BoundAttempt.Value.ToString("N"),
                contentBase64 = "%%%",
            },
            cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, invalidBase64.StatusCode);
        Assert.Equal(
            "invalid-payload-content",
            (await invalidBase64.Content.ReadAsStringAsync(cancellationToken)).Trim());

        using var store = await client.PostAsJsonAsync(
            "/v1/behaviors/broker/payloads/store",
            new
            {
                owner = BoundOwner.Value,
                taskType = BoundTask.Type,
                taskOwner = BoundTask.Owner.Value,
                taskName = BoundTask.Name,
                attempt = BoundAttempt.Value.ToString("N"),
                contentBase64 = Convert.ToBase64String(Plaintext),
            },
            cancellationToken);
        var stored = await store.Content.ReadFromJsonAsync<ProtectedReferenceResponse>(
            cancellationToken: cancellationToken);
        Assert.NotNull(stored);

        using var wrongTask = await client.PostAsJsonAsync(
            "/v1/behaviors/broker/payloads/load",
            new
            {
                owner = BoundOwner.Value,
                taskType = BoundTask.Type,
                taskOwner = BoundTask.Owner.Value,
                taskName = "other-task",
                attempt = BoundAttempt.Value.ToString("N"),
                reference = new { id = stored.Id, expiresAt = stored.ExpiresAt },
            },
            cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, wrongTask.StatusCode);
        Assert.Equal(
            "invalid-protected-reference",
            (await wrongTask.Content.ReadAsStringAsync(cancellationToken)).Trim());

        using var wrongAttempt = await client.PostAsJsonAsync(
            "/v1/behaviors/broker/payloads/load",
            new
            {
                owner = BoundOwner.Value,
                taskType = BoundTask.Type,
                taskOwner = BoundTask.Owner.Value,
                taskName = BoundTask.Name,
                attempt = Guid.NewGuid().ToString("N"),
                reference = new { id = stored.Id, expiresAt = stored.ExpiresAt },
            },
            cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, wrongAttempt.StatusCode);
        Assert.Equal(
            "invalid-protected-reference",
            (await wrongAttempt.Content.ReadAsStringAsync(cancellationToken)).Trim());
    }

    [Fact(DisplayName = "reverse broker load rejects expired and tampered references")]
    public async Task LoadRejectsExpiredAndTamperedReferences()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = new AdjustableTimeProvider(DateTimeOffset.Parse(
            "2026-07-31T12:00:00Z",
            System.Globalization.CultureInfo.InvariantCulture));
        await using var host = await StartHostAsync(cancellationToken, time);
        using var client = CreateClient(host);

        using var store = await client.PostAsJsonAsync(
            "/v1/behaviors/broker/payloads/store",
            new
            {
                owner = BoundOwner.Value,
                taskType = BoundTask.Type,
                taskOwner = BoundTask.Owner.Value,
                taskName = BoundTask.Name,
                attempt = BoundAttempt.Value.ToString("N"),
                contentBase64 = Convert.ToBase64String(Plaintext),
            },
            cancellationToken);
        var stored = await store.Content.ReadFromJsonAsync<ProtectedReferenceResponse>(
            cancellationToken: cancellationToken);
        Assert.NotNull(stored);

        time.Advance(TimeSpan.FromHours(2));
        using var expired = await client.PostAsJsonAsync(
            "/v1/behaviors/broker/payloads/load",
            new
            {
                owner = BoundOwner.Value,
                taskType = BoundTask.Type,
                taskOwner = BoundTask.Owner.Value,
                taskName = BoundTask.Name,
                attempt = BoundAttempt.Value.ToString("N"),
                reference = new { id = stored.Id, expiresAt = stored.ExpiresAt },
            },
            cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, expired.StatusCode);
        Assert.Equal(
            "invalid-protected-reference",
            (await expired.Content.ReadAsStringAsync(cancellationToken)).Trim());

        time.Advance(TimeSpan.FromHours(-3));
        using var liveStore = await client.PostAsJsonAsync(
            "/v1/behaviors/broker/payloads/store",
            new
            {
                owner = BoundOwner.Value,
                taskType = BoundTask.Type,
                taskOwner = BoundTask.Owner.Value,
                taskName = BoundTask.Name,
                attempt = BoundAttempt.Value.ToString("N"),
                contentBase64 = Convert.ToBase64String(Plaintext),
            },
            cancellationToken);
        var live = await liveStore.Content.ReadFromJsonAsync<ProtectedReferenceResponse>(
            cancellationToken: cancellationToken);
        Assert.NotNull(live);

        using var tampered = await client.PostAsJsonAsync(
            "/v1/behaviors/broker/payloads/load",
            new
            {
                owner = BoundOwner.Value,
                taskType = BoundTask.Type,
                taskOwner = BoundTask.Owner.Value,
                taskName = BoundTask.Name,
                attempt = BoundAttempt.Value.ToString("N"),
                reference = new
                {
                    id = live.Id,
                    expiresAt = live.ExpiresAt!.Value.AddMinutes(5),
                },
            },
            cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, tampered.StatusCode);
        Assert.Equal(
            "invalid-protected-reference",
            (await tampered.Content.ReadAsStringAsync(cancellationToken)).Trim());
    }

    [Fact(DisplayName = "reverse broker handlers propagate cancellation without inventing CancellationToken.None")]
    public async Task HandlersPropagateCancellation()
    {
        var live = TestContext.Current.CancellationToken;
        await using var host = await StartHostAsync(live);
        using var client = CreateClient(host);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await client.PostAsJsonAsync(
                "/v1/behaviors/broker/payloads/store",
                new
                {
                    owner = BoundOwner.Value,
                    taskType = BoundTask.Type,
                    taskOwner = BoundTask.Owner.Value,
                    taskName = BoundTask.Name,
                    attempt = BoundAttempt.Value.ToString("N"),
                    contentBase64 = Convert.ToBase64String(Plaintext),
                },
                cancelled.Token));
    }

    [Fact(DisplayName = "durable state behind reverse broker store contains no plaintext secret")]
    public async Task DurableStateBehindHandlersHasNoPlaintext()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var state = new TestDurableValue<byte[]>([]);
        await using var host = await StartHostAsync(cancellationToken, state: state, protector: new XorProtector());
        using var client = CreateClient(host);
        var secret = "handler-secret-must-not-persist"u8.ToArray();

        using var store = await client.PostAsJsonAsync(
            "/v1/behaviors/broker/payloads/store",
            new
            {
                owner = BoundOwner.Value,
                taskType = BoundTask.Type,
                taskOwner = BoundTask.Owner.Value,
                taskName = BoundTask.Name,
                attempt = BoundAttempt.Value.ToString("N"),
                contentBase64 = Convert.ToBase64String(secret),
            },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, store.StatusCode);

        Assert.NotNull(state.Value);
        var durable = Encoding.UTF8.GetString(state.Value);
        Assert.DoesNotContain("handler-secret-must-not-persist", durable, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(state.Value);
        Assert.DoesNotContain(
            Convert.ToBase64String(secret),
            document.RootElement.GetRawText(),
            StringComparison.Ordinal);
    }

    private static HttpClient CreateClient(WebApplication host)
    {
        var address = host.Urls.First();
        return new HttpClient { BaseAddress = new Uri(address) };
    }

    private static async Task<WebApplication> StartHostAsync(
        CancellationToken cancellationToken,
        TimeProvider? time = null,
        TestDurableValue<byte[]>? state = null,
        IDurablePayloadProtector? protector = null)
    {
        var access = new DirectBehaviorProtectedPayloadAccess(
            BoundOwner,
            state ?? new TestDurableValue<byte[]>([]),
            protector ?? new XorProtector(),
            time ?? TimeProvider.System);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton<IBehaviorProtectedPayloadAccess>(access);
        var app = builder.Build();
        app.MapBehaviorProtectedPayloadBroker();
        await app.StartAsync(cancellationToken);
        return app;
    }

    private sealed class DirectBehaviorProtectedPayloadAccess(
        OwnerId owner,
        IDurableValue<byte[]> state,
        IDurablePayloadProtector protector,
        TimeProvider time) : IBehaviorProtectedPayloadAccess
    {
        private readonly DurableProtectedPayloadStore store = new(
            state,
            static () => ValueTask.CompletedTask,
            protector,
            owner,
            time);

        public ValueTask<ProtectedPayloadReference> StoreAsync(
            OwnerId storeOwner,
            NeuronId task,
            AttemptId attempt,
            ReadOnlyMemory<byte> plaintext,
            CancellationToken cancellationToken)
            => store.StoreAsync(storeOwner, task, attempt.Value, plaintext, TimeSpan.FromHours(1), cancellationToken);

        public ValueTask<ReadOnlyMemory<byte>> LoadAsync(
            OwnerId loadOwner,
            NeuronId task,
            AttemptId attempt,
            ProtectedPayloadReference reference,
            CancellationToken cancellationToken)
            => store.LoadAsync(loadOwner, task, attempt.Value, reference, cancellationToken);
    }

    private sealed class XorProtector : IDurablePayloadProtector
    {
        public byte[] Protect(string purpose, ReadOnlySpan<byte> plaintext)
        {
            var protectedPayload = plaintext.ToArray();
            for (var index = 0; index < protectedPayload.Length; index++)
            {
                protectedPayload[index] ^= 0xA5;
            }

            return protectedPayload;
        }

        public byte[] Unprotect(string purpose, ReadOnlySpan<byte> protectedPayload)
            => Protect(purpose, protectedPayload);
    }

    private sealed class AdjustableTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset utcNow = start;

        public override DateTimeOffset GetUtcNow() => utcNow;

        public void Advance(TimeSpan delta) => utcNow += delta;
    }

    private sealed class TestDurableValue<T>(T value) : IDurableValue<T>
    {
        [System.Diagnostics.CodeAnalysis.AllowNull]
        public T Value { get; set; } = value;
    }

    private sealed record ProtectedReferenceResponse(string Id, DateTimeOffset? ExpiresAt);

    private sealed record LoadPayloadResponse(string ContentBase64);
}
