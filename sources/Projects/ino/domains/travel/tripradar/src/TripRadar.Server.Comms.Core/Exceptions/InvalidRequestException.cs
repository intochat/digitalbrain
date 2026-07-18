namespace TripRadar.Server.Comms.Core.Exceptions;

public sealed class InvalidRequestException(string message, Exception? innerException) : ApplicationException(message, innerException)
{
    public InvalidRequestException(string message) : this(message, null)
    {
        ErrorReason = message;
    }

    public string ErrorCode { get; set; } = "INVALID_REQUEST";

    public string ErrorReason { get; set; } = null!;


    public int StatusCode { get; set; } = 400;
}
