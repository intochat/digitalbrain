using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Security;
using DigitalBrain.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
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
    private const string ValidCredential = "unit-test-broker-credential";

    [Fact(DisplayName = "real reverse broker store/load handlers round-trip owner/task/attempt-bound payloads")]
    public async Task StoreLoadHandlersRoundTripBoundPayload()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await StartHostAsync(cancellationToken);
        using var client = host.CreateClient();

        using var store = await PostAuthorizedAsync(
            client,
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

        using var load = await PostAuthorizedAsync(
            client,
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

    [Fact(DisplayName = "missing, blank, and wrong broker credentials fail closed without calling store access")]
    public async Task MissingBlankAndWrongCredentialsFailClosedWithoutStoreAccess()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var access = new RecordingAccess();
        await using var host = await StartHostAsync(cancellationToken, access: access);
        using var client = host.CreateClient();

        var body = new
        {
            owner = BoundOwner.Value,
            taskType = BoundTask.Type,
            taskOwner = BoundTask.Owner.Value,
            taskName = BoundTask.Name,
            attempt = BoundAttempt.Value.ToString("N"),
            contentBase64 = Convert.ToBase64String(Plaintext),
        };

        using var missing = await client.PostAsJsonAsync(
            "/v1/behaviors/broker/payloads/store",
            body,
            cancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
        Assert.Equal("unauthorized", (await missing.Content.ReadAsStringAsync(cancellationToken)).Trim());

        using var blankRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/behaviors/broker/payloads/store")
        {
            Content = JsonContent.Create(body),
        };
        blankRequest.Headers.TryAddWithoutValidation(BehaviorBrokerContract.CredentialHeaderName, "   ");
        using var blank = await client.SendAsync(blankRequest, cancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, blank.StatusCode);
        Assert.Equal("unauthorized", (await blank.Content.ReadAsStringAsync(cancellationToken)).Trim());

        using var wrong = await PostAuthorizedAsync(
            client,
            "/v1/behaviors/broker/payloads/store",
            body,
            cancellationToken,
            credential: "wrong-credential");
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal("unauthorized", (await wrong.Content.ReadAsStringAsync(cancellationToken)).Trim());

        Assert.Equal(0, access.StoreCalls);
        Assert.Equal(0, access.LoadCalls);
    }

    [Fact(DisplayName = "multi-value broker credential header fails closed without store access")]
    public async Task MultiValueCredentialHeaderFailsClosedWithoutStoreAccess()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var access = new RecordingAccess();
        await using var host = await StartHostAsync(cancellationToken, access: access);
        using var client = host.CreateClient();

        var body = new
        {
            owner = BoundOwner.Value,
            taskType = BoundTask.Type,
            taskOwner = BoundTask.Owner.Value,
            taskName = BoundTask.Name,
            attempt = BoundAttempt.Value.ToString("N"),
            contentBase64 = Convert.ToBase64String(Plaintext),
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/behaviors/broker/payloads/store")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.TryAddWithoutValidation(
            BehaviorBrokerContract.CredentialHeaderName,
            [ValidCredential, "smuggled-extra-credential"]);

        using var response = await client.SendAsync(request, cancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("unauthorized", (await response.Content.ReadAsStringAsync(cancellationToken)).Trim());
        Assert.Equal(0, access.StoreCalls);
        Assert.Equal(0, access.LoadCalls);
    }

    [Fact(DisplayName = "correct broker credential authorizes store and reaches access layer")]
    public async Task CorrectCredentialAuthorizesStore()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var access = new RecordingAccess();
        await using var host = await StartHostAsync(cancellationToken, access: access);
        using var client = host.CreateClient();

        using var store = await PostAuthorizedAsync(
            client,
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
        Assert.Equal(1, access.StoreCalls);
    }

    [Fact(DisplayName = "unconfigured broker credential fails closed for all broker requests")]
    public async Task UnconfiguredCredentialFailsClosed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var access = new RecordingAccess();
        await using var host = await StartHostAsync(cancellationToken, access: access, credential: null);
        using var client = host.CreateClient();

        using var response = await PostAuthorizedAsync(
            client,
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
            cancellationToken,
            credential: ValidCredential);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, access.StoreCalls);
    }

    [Fact(DisplayName = "broker credential middleware does not gate unrelated health endpoints")]
    public async Task NonBrokerHealthEndpointRemainsOpenWithoutCredential()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var access = new RecordingAccess();
        await using var host = await StartHostAsync(cancellationToken, access: access);
        using var client = host.CreateClient();

        using var health = await client.GetAsync(new Uri("/health", UriKind.Relative), cancellationToken);
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal("Healthy", (await health.Content.ReadAsStringAsync(cancellationToken)).Trim());

        using var missing = await client.PostAsJsonAsync(
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
        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
        Assert.Equal(0, access.StoreCalls);
    }

    [Fact(DisplayName = "identity-invalid owner slash maps to stable invalid-request without identity prose")]
    public async Task IdentityInvalidOwnerSlashMapsToStableInvalidRequest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var access = new RecordingAccess();
        await using var host = await StartHostAsync(cancellationToken, access: access);
        using var client = host.CreateClient();

        using var response = await PostAuthorizedAsync(
            client,
            "/v1/behaviors/broker/payloads/store",
            new
            {
                owner = "bad/owner",
                taskType = BoundTask.Type,
                taskOwner = "bad/owner",
                taskName = BoundTask.Name,
                attempt = BoundAttempt.Value.ToString("N"),
                contentBase64 = Convert.ToBase64String(Plaintext),
            },
            cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
        Assert.Equal("invalid-request", body);
        Assert.DoesNotContain("Identity parts", body, StringComparison.Ordinal);
        Assert.DoesNotContain("ArgumentException", body, StringComparison.Ordinal);
        Assert.DoesNotContain("grain keys", body, StringComparison.Ordinal);
        Assert.Equal(0, access.StoreCalls);
        Assert.Equal(0, access.LoadCalls);
    }

    [Fact(DisplayName = "reverse broker rejects cross-owner, wrong task/attempt, empty content, and invalid base64")]
    public async Task StoreLoadHandlersRejectIdentityAndContentMisuse()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await StartHostAsync(cancellationToken);
        using var client = host.CreateClient();

        using var missingOwner = await PostAuthorizedAsync(
            client,
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

        using var mismatch = await PostAuthorizedAsync(
            client,
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

        using var empty = await PostAuthorizedAsync(
            client,
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

        using var invalidBase64 = await PostAuthorizedAsync(
            client,
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

        using var store = await PostAuthorizedAsync(
            client,
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

        using var wrongTask = await PostAuthorizedAsync(
            client,
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

        using var wrongAttempt = await PostAuthorizedAsync(
            client,
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
        using var client = host.CreateClient();

        using var store = await PostAuthorizedAsync(
            client,
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
        using var expired = await PostAuthorizedAsync(
            client,
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
        using var liveStore = await PostAuthorizedAsync(
            client,
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

        using var tampered = await PostAuthorizedAsync(
            client,
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
        using var client = host.CreateClient();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await PostAuthorizedAsync(
                client,
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
        using var client = host.CreateClient();
        var secret = "handler-secret-must-not-persist"u8.ToArray();

        using var store = await PostAuthorizedAsync(
            client,
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

    private static async Task<HttpResponseMessage> PostAuthorizedAsync(
        HttpClient client,
        string path,
        object body,
        CancellationToken cancellationToken,
        string? credential = ValidCredential)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body),
        };
        if (credential is not null)
        {
            request.Headers.TryAddWithoutValidation(BehaviorBrokerContract.CredentialHeaderName, credential);
        }

        return await client.SendAsync(request, cancellationToken);
    }

    private static async Task<RunningHost> StartHostAsync(
        CancellationToken cancellationToken,
        TimeProvider? time = null,
        TestDurableValue<byte[]>? state = null,
        IDurablePayloadProtector? protector = null,
        IBehaviorProtectedPayloadAccess? access = null,
        string? credential = ValidCredential)
    {
        access ??= new DirectBehaviorProtectedPayloadAccess(
            BoundOwner,
            state ?? new TestDurableValue<byte[]>([]),
            protector ?? new XorProtector(),
            time ?? TimeProvider.System);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [BehaviorBrokerContract.CredentialConfigurationKey] = credential,
            })
            .Build();

        var port = GetFreeTcpPort();
        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(BehaviorProtectedPayloadBrokerEndpointsTests).Assembly.FullName,
        });
        builder.WebHost.UseKestrel(options => options.Listen(IPAddress.Loopback, port));
        builder.Services.AddRouting();
        builder.Services.AddSingleton(configuration);
        builder.Services.AddSingleton<IConfiguration>(configuration);
        builder.Services.AddBehaviorBrokerAuthentication(configuration);
        builder.Services.AddSingleton(access);
        var app = builder.Build();
        app.UseRouting();
        app.UseBehaviorBrokerAuthentication();
        app.MapGet("/health", () => Results.Text("Healthy"));
        app.MapBehaviorProtectedPayloadBroker();
        await app.StartAsync(cancellationToken);
        return new RunningHost(app, new Uri($"http://127.0.0.1:{port}"));

        static int GetFreeTcpPort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
    }

    private sealed class RunningHost(WebApplication app, Uri baseAddress) : IAsyncDisposable
    {
        public Uri BaseAddress { get; } = baseAddress;

        public HttpClient CreateClient() => new() { BaseAddress = BaseAddress };

        public ValueTask DisposeAsync() => app.DisposeAsync();
    }

    private sealed class RecordingAccess : IBehaviorProtectedPayloadAccess
    {
        public int StoreCalls { get; private set; }
        public int LoadCalls { get; private set; }

        public ValueTask<ProtectedPayloadReference> StoreAsync(
            OwnerId owner,
            NeuronId task,
            AttemptId attempt,
            ReadOnlyMemory<byte> plaintext,
            CancellationToken cancellationToken)
        {
            StoreCalls++;
            return ValueTask.FromResult(
                new ProtectedPayloadReference(Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(1)));
        }

        public ValueTask<ReadOnlyMemory<byte>> LoadAsync(
            OwnerId owner,
            NeuronId task,
            AttemptId attempt,
            ProtectedPayloadReference reference,
            CancellationToken cancellationToken)
        {
            LoadCalls++;
            return ValueTask.FromResult<ReadOnlyMemory<byte>>(Plaintext);
        }
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
