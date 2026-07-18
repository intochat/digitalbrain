using System.Text.RegularExpressions;
using FluentValidation;
using TripRadar.Server.Application.DTO.Requests;

namespace TripRadar.Server.Application.UseCases.SearchEngine.MapsDirections.Queries.GetMapsDirections;

public class GetMapsDirectionsQueryValidator : AbstractValidator<GetMapsDirectionsQuery>
{
    private static readonly int[] AllowedTravelModes = [0, 1, 2, 3, 4, 6, 9];
    private static readonly Regex TimePattern = new("^(depart_at:\\d+|arrive_by:\\d+|last_available)$", RegexOptions.Compiled);

    public GetMapsDirectionsQueryValidator()
    {
        RuleFor(x => x.Username)
            .NotEmpty()
            .MaximumLength(50)
            .WithMessage("Username is required and must not exceed 50 characters.");

        RuleFor(x => x.Request).NotNull();

        RuleFor(x => x.Request)
            .Must(HasValidStart)
            .WithMessage("Exactly one of StartAddr, StartDataId, or StartCoords must be provided.");

        RuleFor(x => x.Request)
            .Must(HasValidEnd)
            .WithMessage("Exactly one of EndAddr, EndDataId, or EndCoords must be provided.");

        RuleFor(x => x.Request.TravelMode)
            .Must(value => value is null || AllowedTravelModes.Contains(value.Value))
            .WithMessage("TravelMode must be one of: 0, 1, 2, 3, 4, 6, 9.");

        RuleFor(x => x.Request.DistanceUnit)
            .Must(value => value is null || value is 0 or 1)
            .WithMessage("DistanceUnit must be 0 (metric) or 1 (imperial).");

        RuleFor(x => x.Request.Route)
            .Must(value => value is null || value is 2 or 3 or 4)
            .WithMessage("Route must be 2, 3, or 4.");

        RuleFor(x => x.Request)
            .Must(request => request.Route is null || request.TravelMode == 3)
            .WithMessage("Route is only supported when TravelMode is 3 (transit).");

        RuleFor(x => x.Request.Time)
            .Must(value => string.IsNullOrWhiteSpace(value) || TimePattern.IsMatch(value))
            .WithMessage("Time must be depart_at:<timestamp>, arrive_by:<timestamp>, or last_available.");

        RuleFor(x => x.Request)
            .Must(request => string.IsNullOrWhiteSpace(request.Time) || request.TravelMode == 3)
            .WithMessage("Time is only supported when TravelMode is 3 (transit).");

        RuleFor(x => x.Request)
            .Must(request => !(request.NoCache == true && request.Async == true))
            .WithMessage("NoCache and Async cannot both be true.");
    }

    private static bool HasValidStart(GetMapsDirectionsRequestDTO request)
    {
        var count = new[] { request.StartAddr, request.StartDataId, request.StartCoords }
            .Count(value => !string.IsNullOrWhiteSpace(value));
        return count == 1;
    }

    private static bool HasValidEnd(GetMapsDirectionsRequestDTO request)
    {
        var count = new[] { request.EndAddr, request.EndDataId, request.EndCoords }
            .Count(value => !string.IsNullOrWhiteSpace(value));
        return count == 1;
    }
}
