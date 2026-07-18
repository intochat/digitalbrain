namespace TripRadar.Server.Comms.Core.Exceptions;

public record ExceptionDetails(int Status, string ErrorCode, string ErrorReason, IEnumerable<object>? Details);
