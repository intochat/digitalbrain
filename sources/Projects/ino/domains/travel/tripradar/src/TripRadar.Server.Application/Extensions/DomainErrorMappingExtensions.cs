using TripRadar.Server.Comms.Core.Errors;
using DomainError = TripRadar.Server.Domain.Rules.DomainError;

namespace TripRadar.Server.Application.Extensions;

public static class DomainErrorMappingExtensions
{
    public static Error ToApplicationError(this DomainError error) => new(error.Code, error.Reason);
}
