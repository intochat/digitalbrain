using FluentValidation;
using TripRadar.Server.Application.DTO.Requests;

namespace TripRadar.Server.Application.UseCases.SearchEngine.MapsPlaceResults.Queries.GetMapsPlaceResults;

public class GetMapsPlaceResultsQueryValidator : AbstractValidator<GetMapsPlaceResultsQuery>
{
    public GetMapsPlaceResultsQueryValidator()
    {
        RuleFor(x => x.Username)
            .NotEmpty()
            .MaximumLength(50)
            .WithMessage("Username is required and must not exceed 50 characters.");

        RuleFor(x => x.Request).NotNull();

        RuleFor(x => x.Request)
            .Must(HasValidPlaceParameters)
            .WithMessage("Provide either PlaceId, DataCid, or Type + Data (Type must be 'place').");

        RuleFor(x => x.Request)
            .Must(request => !(request.NoCache == true && request.Async == true))
            .WithMessage("NoCache and Async cannot both be true.");
    }

    private static bool HasValidPlaceParameters(GetMapsPlaceResultsRequestDTO request)
    {
        if (!string.IsNullOrWhiteSpace(request.PlaceId))
        {
            return string.IsNullOrWhiteSpace(request.Type)
                && string.IsNullOrWhiteSpace(request.Data)
                && string.IsNullOrWhiteSpace(request.DataCid);
        }

        if (!string.IsNullOrWhiteSpace(request.DataCid))
        {
            return string.IsNullOrWhiteSpace(request.Type) && string.IsNullOrWhiteSpace(request.Data);
        }

        if (string.IsNullOrWhiteSpace(request.Type) || string.IsNullOrWhiteSpace(request.Data))
        {
            return false;
        }

        return string.Equals(request.Type, "place", StringComparison.OrdinalIgnoreCase);
    }
}
