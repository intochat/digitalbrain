using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Security;
using DigitalBrain.Tasks;
using Orleans.Journaling;
using Xunit;

namespace DigitalBrain.Behaviors.Tests;

public sealed class BehaviorProtectedTriggerStoreTests
{
    private static readonly OwnerId Owner = new("trigger-owner");
    private static readonly NeuronId TaskNeuron = NeuronId.For<ITask>(Owner, "trigger-task");
    private static readonly BehaviorId Behavior = new("com.digitalbrain.trigger-store");
    private static readonly BehaviorRevisionId Revision = new(
        "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");
    private const string CaseId = "case.SampleTrigger";
    private static readonly byte[] Plaintext = "trigger-secret-value"u8.ToArray();

    [Fact(DisplayName = "trigger store ciphertext omits plaintext; correct activation loads; wrong scope and provider purpose refuse; retry attempt reuses same ref")]
    public async Task TriggerStoreIsActivationScopedRetryStableAndPurposeSeparated()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var state = new TestDurableValue<byte[]>(Array.Empty<byte>());
        var protector = new RecordingPurposeProtector();
        var time = new AdjustableTimeProvider(
            DateTimeOffset.Parse("2026-07-31T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        var store = new DurableProtectedTriggerStore(
            state,
            static () => ValueTask.CompletedTask,
            protector,
            Owner,
            time);

        var reference = await store.StoreAsync(
            Owner,
            TaskNeuron,
            Behavior,
            Revision,
            CaseId,
            Plaintext,
            TimeSpan.FromHours(1),
            cancellationToken);

        Assert.NotEqual(Guid.Empty, reference.Id);
        Assert.True(reference.ExpiresAt > time.GetUtcNow());
        Assert.DoesNotContain(Plaintext, state.Value ?? []);
        Assert.DoesNotContain(Encoding.UTF8.GetString(Plaintext), Encoding.UTF8.GetString(state.Value ?? []), StringComparison.Ordinal);
        Assert.Contains("ProtectedTriggerStore/v1/", protector.LastPurpose, StringComparison.Ordinal);
        Assert.DoesNotContain(Guid.NewGuid().ToString("N"), protector.LastPurpose, StringComparison.Ordinal);

        var loaded = await store.LoadAsync(
            Owner,
            TaskNeuron,
            Behavior,
            Revision,
            CaseId,
            reference,
            cancellationToken);
        Assert.Equal(Plaintext, loaded.ToArray());

        // Same activation identity works for a fresh retry attempt (no attempt in purpose).
        var reloaded = await store.LoadAsync(
            Owner,
            TaskNeuron,
            Behavior,
            Revision,
            CaseId,
            reference,
            cancellationToken);
        Assert.Equal(Plaintext, reloaded.ToArray());

        await Assert.ThrowsAsync<CryptographicException>(() => store.LoadAsync(
            new OwnerId("other-owner"),
            TaskNeuron,
            Behavior,
            Revision,
            CaseId,
            reference,
            cancellationToken).AsTask());

        await Assert.ThrowsAsync<CryptographicException>(() => store.LoadAsync(
            Owner,
            NeuronId.For<ITask>(Owner, "other-task"),
            Behavior,
            Revision,
            CaseId,
            reference,
            cancellationToken).AsTask());

        await Assert.ThrowsAsync<CryptographicException>(() => store.LoadAsync(
            Owner,
            TaskNeuron,
            new BehaviorId("com.digitalbrain.other"),
            Revision,
            CaseId,
            reference,
            cancellationToken).AsTask());

        await Assert.ThrowsAsync<CryptographicException>(() => store.LoadAsync(
            Owner,
            TaskNeuron,
            Behavior,
            new BehaviorRevisionId("ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff"),
            CaseId,
            reference,
            cancellationToken).AsTask());

        await Assert.ThrowsAsync<CryptographicException>(() => store.LoadAsync(
            Owner,
            TaskNeuron,
            Behavior,
            Revision,
            "case.Other",
            reference,
            cancellationToken).AsTask());

        // Provider-operation purpose store cannot load a trigger reference (and vice versa).
        var payloadState = new TestDurableValue<byte[]>(Array.Empty<byte>());
        var payloadStore = new DurableProtectedPayloadStore(
            payloadState,
            static () => ValueTask.CompletedTask,
            protector,
            Owner,
            time);
        var attempt = Guid.Parse("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        var payloadRef = await payloadStore.StoreAsync(
            Owner,
            TaskNeuron,
            attempt,
            "provider-secret"u8.ToArray(),
            TimeSpan.FromHours(1),
            cancellationToken);

        await Assert.ThrowsAsync<CryptographicException>(() => store.LoadAsync(
            Owner,
            TaskNeuron,
            Behavior,
            Revision,
            CaseId,
            payloadRef,
            cancellationToken).AsTask());

        await Assert.ThrowsAsync<CryptographicException>(() => payloadStore.LoadAsync(
            Owner,
            TaskNeuron,
            attempt,
            reference,
            cancellationToken).AsTask());
    }

    [Fact(DisplayName = "storing one activation trigger twice returns the identical reference, so a redelivered wake carries the Start payload the task already receipted")]
    public async Task StoringTheSameActivationTriggerTwiceReturnsTheIdenticalReference()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var state = new TestDurableValue<byte[]>(Array.Empty<byte>());
        var time = new AdjustableTimeProvider(
            DateTimeOffset.Parse("2026-07-31T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        var store = new DurableProtectedTriggerStore(
            state,
            static () => ValueTask.CompletedTask,
            new RecordingPurposeProtector(),
            Owner,
            time);

        var first = await store.StoreAsync(
            Owner,
            TaskNeuron,
            Behavior,
            Revision,
            CaseId,
            Plaintext,
            TimeSpan.FromHours(1),
            cancellationToken);

        time.Advance(TimeSpan.FromMinutes(5));
        var redelivered = await store.StoreAsync(
            Owner,
            TaskNeuron,
            Behavior,
            Revision,
            CaseId,
            Plaintext,
            TimeSpan.FromHours(1),
            cancellationToken);

        // Both the id and the expiry, because the whole reference travels inside the Start payload
        // the Task receipts, and TaskNeuron.Start refuses a receipted CommandId whose payload moved.
        Assert.Equal(first, redelivered);

        var otherActivation = await store.StoreAsync(
            Owner,
            NeuronId.For<ITask>(Owner, "other-trigger-task"),
            Behavior,
            Revision,
            CaseId,
            Plaintext,
            TimeSpan.FromHours(1),
            cancellationToken);
        Assert.NotEqual(first.Id, otherActivation.Id);

        var loaded = await store.LoadAsync(
            Owner,
            TaskNeuron,
            Behavior,
            Revision,
            CaseId,
            first,
            cancellationToken);
        Assert.Equal(Plaintext, loaded.ToArray());
    }

    private sealed class RecordingPurposeProtector : IDurablePayloadProtector
    {
        public string LastPurpose { get; private set; } = "";

        public byte[] Protect(string purpose, ReadOnlySpan<byte> plaintext)
        {
            LastPurpose = purpose;
            var protectedPayload = new byte[plaintext.Length + 1];
            protectedPayload[0] = 0x7E;
            plaintext.CopyTo(protectedPayload.AsSpan(1));
            return protectedPayload;
        }

        public byte[] Unprotect(string purpose, ReadOnlySpan<byte> protectedPayload)
        {
            LastPurpose = purpose;
            if (protectedPayload.Length == 0 || protectedPayload[0] != 0x7E)
            {
                throw new CryptographicException("invalid envelope");
            }

            return protectedPayload[1..].ToArray();
        }
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
}
