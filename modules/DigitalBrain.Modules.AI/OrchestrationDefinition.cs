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

internal sealed record OrchestrationDefinition(string Fingerprint)
{
    internal const int CurrentFormatVersion = 2;

    internal string HostId => $"dba_{Fingerprint}";

    internal string HostName => $"orchestration_{Fingerprint}";

    internal static void RequireMatch(
        OrchestrationDefinition stored,
        OrchestrationDefinition current)
    {
        ArgumentNullException.ThrowIfNull(stored);
        ArgumentNullException.ThrowIfNull(current);

        if (!string.Equals(stored.Fingerprint, current.Fingerprint, StringComparison.Ordinal))
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
        var orchestrationTypeName = orchestrationType.AssemblyQualifiedName
            ?? throw new InvalidOperationException("The orchestration type has no assembly-qualified identity.");
        OrchestrationParticipant[] described =
            [.. participants.Select(MafParticipantAdapter.Describe)];
        var source = new FingerprintSource(
            CurrentFormatVersion,
            identity.KindName,
            orchestrationTypeName,
            AssemblyIdentity(orchestrationType.Assembly, requireVersion: true),
            $"{AssemblyIdentity(typeof(AIAgent).Assembly, requireVersion: false)};{AssemblyIdentity(typeof(AgentWorkflowBuilder).Assembly, requireVersion: false)}",
            identity.ExecutionEnvironmentName,
            identity.ManagerName(described.Length),
            identity.AggregatorName,
            "fingerprint-v2",
            described);
        var fingerprint = Convert.ToHexStringLower(
            SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(source)));

        return new(fingerprint);
    }

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
