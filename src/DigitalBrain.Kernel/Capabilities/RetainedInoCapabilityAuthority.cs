using System.Security.Cryptography;
using System.Text.Json;
using DigitalBrain.Core.Runtime;
using DigitalBrain.Integrations.Google.Contracts;
using DigitalBrain.Integrations.Salesforce.Contracts;
using DigitalBrain.Kernel.Contracts;
using DigitalBrain.Kernel.Runtime;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Kernel.Capabilities;

public static class RetainedInoCapabilityAuthority
{
    public static readonly FeatureInstallationId InstallationId = new("retained-ino");
    public static readonly ReleaseDigest ReleaseDigest = new(
        Convert.ToHexStringLower(SHA256.HashData("digitalbrain.retained-ino.v3"u8)));
    public static readonly GrantRevision GrantRevision = new(1);

    public static CapabilityRequest CreateRequest(
        BrainOwnerId ownerId,
        ActorId actorId,
        string inputId,
        string logicalOperationKey,
        string capabilityId,
        JsonElement payload,
        DateTimeOffset now,
        string correlationId,
        string? causationId = null) =>
        new(
            ownerId,
            actorId,
            InstallationId,
            ReleaseDigest,
            inputId,
            logicalOperationKey,
            capabilityId,
            1,
            Connection(ownerId, actorId, Provider(capabilityId)),
            GrantRevision,
            payload,
            now.AddSeconds(30),
            correlationId,
            causationId);

    public static ProviderConnectionId Connection(BrainOwnerId ownerId, ActorId actorId, string provider) =>
        new($"{provider}-{RequestScope.Id(ownerId, actorId)}");

    public static string Provider(string capabilityId) => capabilityId switch
    {
        GoogleCapabilityIds.GmailMessageRead or GoogleCapabilityIds.GmailMailboxRead or
            GoogleCapabilityIds.GmailSendPropose => "google",
        SalesforceCapabilityIds.RecordRead or SalesforceCapabilityIds.RecordUpdatePropose => "salesforce",
        _ => throw new ArgumentException("The capability is not part of retained INO.", nameof(capabilityId))
    };

    public static string[] AllowedTools(string capabilityId) => capabilityId switch
    {
        GoogleCapabilityIds.GmailMessageRead => [GmailTools.ReadIncomingAtOffset, GmailTools.SummarizeIncoming, GmailTools.SummarizeThread],
        GoogleCapabilityIds.GmailMailboxRead => [GmailTools.ReadMessages, GmailTools.ReadMailboxOverview, GmailTools.ReadThreads],
        GoogleCapabilityIds.GmailSendPropose => [GmailTools.Send],
        SalesforceCapabilityIds.RecordRead =>
        [
            SalesforceTools.DiscoverObjects,
            SalesforceTools.ReadRecords,
            SalesforceTools.SearchRecords,
            SalesforceTools.AggregateRecords,
            SalesforceTools.ContinueRecords,
            SalesforceTools.ReadCurrentProfile
        ],
        SalesforceCapabilityIds.RecordUpdatePropose => [SalesforceTools.UpdateRecord],
        _ => []
    };

    public static bool Matches(CapabilityRequest request)
    {
        string provider;
        try
        {
            provider = Provider(request.CapabilityId);
        }
        catch (ArgumentException)
        {
            return false;
        }
        return request.InstallationId == InstallationId && request.ReleaseDigest == ReleaseDigest &&
               request.CapabilityVersion == 1 && request.GrantRevision == GrantRevision &&
               request.ProviderConnectionId == Connection(request.OwnerId, request.ActorId, provider);
    }
}

public sealed class RetainedInoCapabilityGrantSource(IConfiguration configuration) : ICapabilityGrantSource
{
    public const string EnabledKey = "DigitalBrain:Tools:Enabled";
    public const string PausedKey = "DigitalBrain:Capabilities:RetainedIno:Paused";

    public ValueTask<CapabilityGrant?> ReadAsync(
        CapabilityRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Enabled() || !RetainedInoCapabilityAuthority.Matches(request))
            return ValueTask.FromResult<CapabilityGrant?>(null);

        var grant = new CapabilityGrant(
            request.OwnerId,
            RetainedInoCapabilityAuthority.InstallationId,
            RetainedInoCapabilityAuthority.ReleaseDigest,
            request.CapabilityId,
            1,
            request.ProviderConnectionId,
            RetainedInoCapabilityAuthority.GrantRevision,
            JsonSerializer.SerializeToElement(new
            {
                allowedToolIds = RetainedInoCapabilityAuthority.AllowedTools(request.CapabilityId)
            }),
            enabled: true,
            paused: Paused());
        return ValueTask.FromResult<CapabilityGrant?>(grant);
    }

    private bool Enabled() => bool.TryParse(configuration[EnabledKey], out var enabled) && enabled;
    private bool Paused() => bool.TryParse(configuration[PausedKey], out var paused) && paused;
}
