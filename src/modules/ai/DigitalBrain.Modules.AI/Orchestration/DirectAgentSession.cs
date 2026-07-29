using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Security;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;

namespace DigitalBrain.AI;

internal sealed class DirectAgentSession(
    IDurableValue<byte[]> state,
    IDurablePayloadProtector protector,
    Func<ValueTask> commit,
    NeuronId neuron)
{
    private const int CurrentEnvelopeVersion = 2;
    private const string ProtectionPurposeRoot = "DigitalBrain.AI.DirectAgentSession";

    internal static DirectAgentSession Create(
        IServiceProvider services,
        string stateName,
        Func<ValueTask> commit,
        NeuronId neuron)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(stateName);
        ArgumentNullException.ThrowIfNull(commit);

        return new(
            services.GetRequiredKeyedService<IDurableValue<byte[]>>(stateName),
            services.GetRequiredService<IDurablePayloadProtector>(),
            commit,
            neuron);
    }

    internal bool HasDurableSession => state.Value is { Length: > 0 };

    internal async IAsyncEnumerable<ChatResponseUpdate> RunStreamingAsync(
        AIAgent agent,
        OrchestrationDefinition definition,
        IReadOnlyList<ChatMessage> messages,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(messages);

        var session = state.Value is { Length: > 0 } serialized
            ? await RestoreAsync(agent, serialized, definition, cancellationToken)
            : await agent.CreateSessionAsync(cancellationToken);

        await foreach (var update in agent.RunStreamingAsync(messages, session, cancellationToken: cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            yield return update.AsChatResponseUpdate();
        }

        cancellationToken.ThrowIfCancellationRequested();

        await PersistSessionAsync(agent, session, definition, cancellationToken);
    }

    private async Task PersistSessionAsync(
        AIAgent agent,
        AgentSession session,
        OrchestrationDefinition definition,
        CancellationToken cancellationToken)
    {
        var serializedSession = await agent.SerializeSessionAsync(session, cancellationToken: cancellationToken);
        var protectedSession = protector.Protect(
            Purpose(definition.Fingerprint),
            Encoding.UTF8.GetBytes(serializedSession.GetRawText()));
        var envelope = new DirectAgentSessionEnvelope(CurrentEnvelopeVersion, definition, protectedSession);
        var previous = state.Value?.ToArray();

        state.Value = JsonSerializer.SerializeToUtf8Bytes(envelope);

        try
        {
            await commit();
        }
        catch
        {
            state.Value = previous;
            throw;
        }
    }

    private async Task<AgentSession> RestoreAsync(
        AIAgent agent,
        byte[] serialized,
        OrchestrationDefinition definition,
        CancellationToken cancellationToken)
    {
        DirectAgentSessionEnvelope stored;

        try
        {
            stored = JsonSerializer.Deserialize<DirectAgentSessionEnvelope>(serialized)
                ?? throw RecoveryRequired();
        }
        catch (Exception failure) when (failure is JsonException or NotSupportedException)
        {
            throw RecoveryRequired(failure);
        }

        if (stored.EnvelopeVersion != CurrentEnvelopeVersion
            || stored.Definition is null
            || stored.ProtectedSession is null)
        {
            throw RecoveryRequired();
        }

        if (!string.Equals(stored.Definition.Fingerprint, definition.Fingerprint, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The durable direct-agent session is incompatible with the current orchestration definition; an explicit migration or reset is required.");
        }

        try
        {
            var sessionBytes = protector.Unprotect(Purpose(definition.Fingerprint), stored.ProtectedSession);
            using var sessionJson = JsonDocument.Parse(sessionBytes);

            return await agent.DeserializeSessionAsync(
                sessionJson.RootElement.Clone(),
                cancellationToken: cancellationToken);
        }
        catch (Exception failure) when (failure is CryptographicException
            or JsonException
            or FormatException
            or InvalidOperationException
            or NotSupportedException)
        {
            throw RecoveryRequired(failure);
        }
    }

    private string Purpose(string definitionFingerprint)
        => $"{ProtectionPurposeRoot}.v{CurrentEnvelopeVersion}\n{neuron}\n{definitionFingerprint}";

    private static InvalidOperationException RecoveryRequired(Exception? failure = null)
        => new(
            "The durable direct-agent session cannot be restored; an explicit migration or reset is required.",
            failure);

    private sealed record DirectAgentSessionEnvelope(
        int EnvelopeVersion,
        OrchestrationDefinition Definition,
        byte[] ProtectedSession);
}
