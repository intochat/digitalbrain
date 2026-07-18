using TripRadar.Server.Comms.Core.Errors;

namespace TripRadar.Server.Comms.Core.Exceptions;

public sealed class ValidationException(IEnumerable<ValidationError> errors) : Exception
{
    public IEnumerable<ValidationError> Errors { get; } = errors;

    public override string ToString() => $"Validation errors: {string.Join(", ", Errors.Select(error => error.ErrorMessage))}";
}
