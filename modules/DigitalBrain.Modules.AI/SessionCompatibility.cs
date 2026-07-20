using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Agents.AI.Workflows;

namespace DigitalBrain.AI;

internal static class SessionCompatibility
{
    internal const int CurrentFormatVersion = 1;

    internal static OrchestrationDefinition Describe(
        Type orchestrationType,
        IReadOnlyList<Participant> participants)
    {
        ArgumentNullException.ThrowIfNull(orchestrationType);
        ArgumentNullException.ThrowIfNull(participants);

        var identities = participants.Select(MafParticipantAdapter.Describe).ToArray();
        var mafVersion = AssemblyIdentity(typeof(AgentWorkflowBuilder).Assembly);
        var manager = new ManagerDefinition("round-robin", identities.Length);
        var typeIdentity = $"{orchestrationType.AssemblyQualifiedName}|{orchestrationType.Module.ModuleVersionId:D}";
        var source = new FingerprintSource(
            "group-chat",
            typeIdentity,
            mafVersion,
            "in-process-lockstep",
            "fingerprint-v1",
            manager,
            identities);
        var fingerprint = Convert.ToHexStringLower(
            SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(source)));

        return new(
            CurrentFormatVersion,
            mafVersion,
            fingerprint,
            identities,
            $"dbg_{fingerprint}",
            $"group_{fingerprint}");
    }

    internal static void RequireMatch(
        OrchestrationState stored,
        OrchestrationDefinition current)
    {
        ArgumentNullException.ThrowIfNull(stored);
        ArgumentNullException.ThrowIfNull(current);

        if (stored.FormatVersion != current.FormatVersion
            || !string.Equals(stored.MafVersion, current.MafVersion, StringComparison.Ordinal)
            || !string.Equals(stored.Fingerprint, current.Fingerprint, StringComparison.Ordinal)
            || stored.Participants is null
            || stored.ProtectedSession is null
            || !stored.Participants.SequenceEqual(current.Participants))
        {
            throw new InvalidOperationException(
                "The durable group-chat session is incompatible with the current orchestration definition; an explicit migration or reset is required.");
        }
    }

    private static string AssemblyIdentity(Assembly assembly)
    {
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";

        return $"{assembly.GetName().Name}/{version}";
    }

    private sealed record ManagerDefinition(string Algorithm, int MaximumIterationCount);

    private sealed record FingerprintSource(
        string Kind,
        string OrchestrationType,
        string MafVersion,
        string ExecutionEnvironment,
        string HostIdentityScheme,
        ManagerDefinition Manager,
        IReadOnlyList<OrchestrationParticipant> Participants);
}
