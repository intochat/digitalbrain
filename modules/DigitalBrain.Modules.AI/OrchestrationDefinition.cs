using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using DigitalBrain.Abstractions;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;

namespace DigitalBrain.AI;

internal sealed record OrchestrationParticipant(
    string Contract,
    NeuronId NeuronId,
    string AgentId,
    string AgentName);

internal sealed record OrchestrationDefinition(
    int FormatVersion,
    string MafVersion,
    string Fingerprint,
    OrchestrationParticipant[] Participants,
    string HostId,
    string HostName,
    string Kind,
    string OrchestrationType,
    string ExecutionEnvironment,
    string Manager,
    string Aggregator,
    string ApplicationVersion)
{
    internal const int CurrentFormatVersion = 2;

    private static OrchestrationDefinition Create(
        string orchestrationType,
        string mafVersion,
        DirectOrchestrationIdentity identity,
        OrchestrationParticipant[] participants,
        string applicationVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orchestrationType);
        ArgumentException.ThrowIfNullOrWhiteSpace(mafVersion);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(participants);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationVersion);

        if (participants.Length == 0)
        {
            throw new InvalidOperationException("An orchestration requires at least one participant.");
        }

        var kind = identity.KindName;
        var executionEnvironment = identity.ExecutionEnvironmentName;
        var manager = identity.ManagerName(participants.Length);
        var aggregator = identity.AggregatorName;
        var source = new FingerprintSource(
            CurrentFormatVersion,
            kind,
            orchestrationType,
            applicationVersion,
            mafVersion,
            executionEnvironment,
            manager,
            aggregator,
            "fingerprint-v2",
            participants);
        var fingerprint = Convert.ToHexStringLower(
            SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(source)));

        return new(
            CurrentFormatVersion,
            mafVersion,
            fingerprint,
            participants,
            $"dba_{fingerprint}",
            $"orchestration_{fingerprint}",
            kind,
            orchestrationType,
            executionEnvironment,
            manager,
            aggregator,
            applicationVersion);
    }

    internal static void RequireMatch(
        OrchestrationDefinition stored,
        OrchestrationDefinition current)
    {
        ArgumentNullException.ThrowIfNull(stored);
        ArgumentNullException.ThrowIfNull(current);

        if (!string.Equals(stored.Fingerprint, current.Fingerprint, StringComparison.Ordinal)
            || !string.Equals(stored.HostId, current.HostId, StringComparison.Ordinal)
            || !string.Equals(stored.HostName, current.HostName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The durable direct-agent session is incompatible with the current orchestration definition; an explicit migration or reset is required.");
        }
    }

    internal static OrchestrationDefinition Describe(
        Type orchestrationType,
        IReadOnlyList<Participant> participants,
        DirectOrchestrationIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(orchestrationType);
        ArgumentNullException.ThrowIfNull(participants);
        ArgumentNullException.ThrowIfNull(identity);

        return Create(
            orchestrationType.AssemblyQualifiedName
                ?? throw new InvalidOperationException("The orchestration type has no assembly-qualified identity."),
            MafAssemblyIdentity(),
            identity,
            [.. participants.Select(MafParticipantAdapter.Describe)],
            ApplicationAssemblyIdentity(orchestrationType.Assembly));
    }

    private static string ApplicationAssemblyIdentity(Assembly assembly)
        => AssemblyIdentity(assembly, requireVersion: true);

    private static string MafAssemblyIdentity()
        => $"{AssemblyIdentity(typeof(AIAgent).Assembly, requireVersion: false)};{AssemblyIdentity(typeof(AgentWorkflowBuilder).Assembly, requireVersion: false)}";

    private static string AssemblyIdentity(Assembly assembly, bool requireVersion)
    {
        var informationalVersion =
            assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var fileVersion = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version;
        var assemblyVersion = assembly.GetName().Version?.ToString();
        var version = new[] { informationalVersion, fileVersion, assemblyVersion }
            .FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate));

        if (version is null)
        {
            if (requireVersion)
            {
                throw new InvalidOperationException(
                    $"Assembly '{assembly.GetName().Name}' has no deterministic version identity.");
            }

            version = "unknown";
        }

        return $"{assembly.GetName().Name}/{version}";
    }

    private sealed record FingerprintSource(
        int FormatVersion,
        string Kind,
        string OrchestrationType,
        string ApplicationVersion,
        string MafVersion,
        string ExecutionEnvironment,
        string Manager,
        string Aggregator,
        string HostIdentityScheme,
        IReadOnlyList<OrchestrationParticipant> Participants);
}
