using TripRadar.Server.Comms.Core.Constants;

namespace TripRadar.Server.Comms.Core.Errors;

public record Error(string Code, string Reason)
{
    public static readonly Error None = new(string.Empty, string.Empty);

    public static readonly Error ObjectNotFound = new(ErrorCodes.ObjectNotFound, "Object not found.");

    public static readonly Error CommunicationError = new(ErrorCodes.CommunicationError, "Communication error occurred.");

    public static readonly Error InternalServerError = new(ErrorCodes.InternalError, "Internal Server Error.");

    public static readonly Error ValidationError = new(ErrorCodes.ValidationError, "Validation Error.");
}
