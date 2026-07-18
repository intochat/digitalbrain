using TripRadar.Server.Comms.Core.Exceptions;

namespace TripRadar.Server.Comms.Core.Contracts.Exceptions;

public interface IExceptionDetails
{
    ExceptionDetails? GetExceptionDetails(Exception exception);
}
