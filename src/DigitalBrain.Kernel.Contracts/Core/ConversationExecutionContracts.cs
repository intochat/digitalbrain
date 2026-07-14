using DigitalBrain.Kernel.Contracts;

namespace DigitalBrain.Kernel.Contracts.Runtime;

public enum ExternalAuthorizationResolutionState { Waiting, Ready, Failed }

[GenerateSerializer, Alias("digitalbrain.v2.external-authorization-resolution")]
public sealed record ExternalAuthorizationResolution([property: Id(0)] ExternalAuthorizationResolutionState State, [property: Id(1)] string? SafeReason = null);

public interface IExternalAuthorizationResolver
{
    string Provider { get; }

    Task<ExternalAuthorizationResolution> ResolveAsync(BrainOwnerId ownerId, ActorId actorId, CancellationToken cancellationToken = default);
}
