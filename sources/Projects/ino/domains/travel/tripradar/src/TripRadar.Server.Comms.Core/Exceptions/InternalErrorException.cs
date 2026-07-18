namespace TripRadar.Server.Comms.Core.Exceptions;

public sealed class InternalErrorException(string message, Exception? innerException = null) : ApplicationException(message, innerException)
{
    public string ErrorCode { get; set; } = "INTERNAL_ERROR";

    public string ErrorReason { get; set; } = message;

    public int StatusCode { get; set; } = 500;
}
