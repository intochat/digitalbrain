namespace TripRadar.Server.Comms.Core.Exceptions;

public sealed class ObjectNotFoundException(string message, Exception? innerException = null) : ApplicationException(message, innerException)
{
    public string ErrorCode { get; set; } = "OBJECT_NOT_FOUND";

    public string ErrorReason { get; set; } = message;

    public int StatusCode { get; set; } = 404;
}
