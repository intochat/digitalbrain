using DigitalBrain.Abstractions.Commands;

namespace DigitalBrain.Kernel;

internal static class EdgeResults
{
    public static bool IsNeuronName(string? name)
        => name is { Length: > 0 and <= 128 }
            && !name.Any(character => char.IsWhiteSpace(character) || character is '/' or ':');

    public static bool IsRejection(Exception error)
        => error is ArgumentException or CommandRejectedException or CommandFailedException;

    public static IResult Rejected(Exception error)
        => Results.Problem(error.Message, statusCode: StatusCodes.Status400BadRequest);
}
