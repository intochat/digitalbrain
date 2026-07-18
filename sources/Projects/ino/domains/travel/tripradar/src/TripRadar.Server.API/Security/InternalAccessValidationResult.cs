namespace TripRadar.Server.API.Security;

internal readonly record struct InternalAccessValidationResult(bool IsAuthorized, bool IsMissingApiKey);
