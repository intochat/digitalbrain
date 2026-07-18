namespace TripRadar.Server.Comms.Core.Errors;

public sealed record ValidationError(string PropertyName, string ErrorMessage)
{
    public override string ToString() => $"'$.{PropertyName}' - {ErrorMessage}";
}
