using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Contracts.Types;
using DigitalBrain.Kernel.Enforcement;
using DigitalBrain.Platform.Contracts.Integrations;
using DigitalBrain.Platform.Contracts.Secrets;
using DigitalBrain.Platform.Secrets;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Platform.Integrations;

internal sealed class RegistrationCredentials(IGrainFactory grains, ILogger<RegistrationCredentials> logger)
{
    public Task<SecretRef> Store(IntegrationDefinition definition, string field, string value)
        => grains.GetGrain<ISecrets>(IntegrationVault.Owner).Set(Authorize(definition, field, "store"),
            IntegrationVault.SecretName(definition.Id, field), $"{definition.Id} {field}", value);

    public Task Remove(IntegrationDefinition definition, string field)
        => grains.GetGrain<ISecrets>(IntegrationVault.Owner).Remove(Authorize(definition, field, "remove"), IntegrationVault.SecretName(definition.Id, field));

    private CallerContext Authorize(IntegrationDefinition definition, string field, string operation)
    {
        if (!definition.AllFields.Contains(field, StringComparer.Ordinal)) { throw new ArgumentException("Unknown registration field.", nameof(field)); }
        var actor = CallerContextStamper.TryGet(out var current) ? current : new CallerContext
        {
            PrincipalId = IntegrationVault.CallerAppId,
            AccountId = IntegrationVault.Owner,
            BrainId = IntegrationVault.Owner,
            Kind = CallerKind.Platform,
            StampedBy = TrustedEdge.Platform
        };
        if (actor.Kind == CallerKind.App || actor.StampedBy == TrustedEdge.AppProxy)
        { throw new UnauthorizedAccessException("Installed apps cannot configure provider registrations."); }
        logger.LogInformation("Registration {Operation} for {Provider}/{Field} requested by {Actor} in {Account}/{Brain}.",
            operation, definition.Id, field, actor.PrincipalId, actor.AccountId, actor.BrainId);
        return actor with { Kind = CallerKind.Platform, StampedBy = TrustedEdge.Platform, AppId = IntegrationVault.CallerAppId };
    }
}
