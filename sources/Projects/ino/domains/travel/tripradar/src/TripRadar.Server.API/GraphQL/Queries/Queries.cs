using System.Security.Claims;
using AutoMapper;
using HotChocolate.Authorization;
using MediatR;
using TripRadar.Server.API.Contracts.Requests.Get;
using TripRadar.Server.API.Contracts.Responses.Get;
using TripRadar.Server.Application.DTO.Requests;
using TripRadar.Server.Application.UseCases.PlaceReviews.Queries.GetPlaceReviews;
using TripRadar.Server.Application.UseCases.SearchEngine.Events.Queries.GetEvents;
using TripRadar.Server.Application.UseCases.SearchEngine.Flights.Queries.GetFlightExplore;
using TripRadar.Server.Application.UseCases.SearchEngine.Flights.Queries.GetFlightPriceCalendar;
using TripRadar.Server.Application.UseCases.SearchEngine.Flights.Queries.GetFlights;
using TripRadar.Server.Application.UseCases.SearchEngine.GoogleLightSearch.Queries.GetGoogleLightSearch;
using TripRadar.Server.Application.UseCases.SearchEngine.Hotels.Queries.GetHotels;
using TripRadar.Server.Application.UseCases.SearchEngine.LocalPlaces.Queries.GetLocalPlaces;
using TripRadar.Server.Application.UseCases.SearchEngine.Maps.Queries.GetMaps;
using TripRadar.Server.Application.UseCases.SearchEngine.MapsDirections.Queries.GetMapsDirections;
using TripRadar.Server.Application.UseCases.SearchEngine.MapsPlaceResults.Queries.GetMapsPlaceResults;
using TripRadar.Server.Application.UseCases.SearchEngine.OpenTableReviews.Queries.GetOpenTableReviews;
using TripRadar.Server.Application.UseCases.SearchEngine.TripAdvisorPlace.Queries.GetTripAdvisorPlace;
using TripRadar.Server.Application.UseCases.SearchEngine.TripAdvisorSearch.Queries.GetTripAdvisorSearch;
using TripRadar.Server.Application.UseCases.SearchEngine.YelpPlace.Queries.GetYelpPlace;
using TripRadar.Server.Application.UseCases.SearchEngine.YelpPlaceFullMenu.Queries.GetYelpPlaceFullMenu;
using TripRadar.Server.Application.UseCases.SearchEngine.YelpReviews.Queries.GetYelpReviews;
using TripRadar.Server.Application.UseCases.SearchEngine.YelpSearch.Queries.GetYelpSearch;
using TripRadar.Server.Application.UseCases.SearchEngine.YouTubeSearch.Queries.GetYouTubeSearch;
using TripRadar.Server.Comms.Core.Extensions;

namespace TripRadar.Server.API.GraphQL.Queries;

[Authorize(Policy = "GraphQLAuth")]
[ExtendObjectType("Query")]
public class Queries : BaseQuery
{
    [GraphQLName("events")]
    public async Task<GetEventsResponse> GetEvent([GraphQLName("request")] GetEventRequest request, [Service] IMediator mediator, [Service] IMapper mapper, ClaimsPrincipal claimsPrincipal, CancellationToken cancellationToken = default) =>
        await ExecuteQueryAsync(() => mediator.Send(new GetEventsQuery(mapper.Map<GetEventRequestDTO>(request), claimsPrincipal.GetUsername(), request.TripVaultName), cancellationToken), mapper.Map<GetEventsResponse>);

    [GraphQLName("flights")]
    public async Task<GetFlightsResponse> GetFlight([GraphQLName("request")] GetFlightRequest request, [Service] IMediator mediator, [Service] IMapper mapper, ClaimsPrincipal claimsPrincipal, CancellationToken cancellationToken = default) =>
        await ExecuteQueryAsync(() => mediator.Send(new GetFlightsQuery(mapper.Map<GetFlightRequestDTO>(request), claimsPrincipal.GetUsername(), request.TripVaultName), cancellationToken), mapper.Map<GetFlightsResponse>);

    [GraphQLName("flightExplore")]
    public async Task<GetFlightExploreResponse> GetFlightExplore([GraphQLName("request")] GetFlightExploreRequest request, [Service] IMediator mediator, [Service] IMapper mapper, ClaimsPrincipal claimsPrincipal, CancellationToken cancellationToken = default) =>
        await ExecuteQueryAsync(() => mediator.Send(new GetFlightExploreQuery(mapper.Map<GetFlightExploreRequestDTO>(request), claimsPrincipal.GetUsername()), cancellationToken), mapper.Map<GetFlightExploreResponse>);

    [GraphQLName("flightPriceCalendar")]
    public async Task<GetFlightPriceCalendarResponse> GetFlightPriceCalendar([GraphQLName("request")] GetFlightPriceCalendarRequest request, [Service] IMediator mediator, [Service] IMapper mapper, ClaimsPrincipal claimsPrincipal, CancellationToken cancellationToken = default) =>
        await ExecuteQueryAsync(() => mediator.Send(new GetFlightPriceCalendarQuery(mapper.Map<GetFlightPriceCalendarRequestDTO>(request), claimsPrincipal.GetUsername()), cancellationToken), mapper.Map<GetFlightPriceCalendarResponse>);


    [GraphQLName("googleLightSearch")]
    public async Task<GetGoogleLightSearchResponse> GetGoogleLightSearch([GraphQLName("request")] GetGoogleLightSearchRequest request, [Service] IMediator mediator, [Service] IMapper mapper, ClaimsPrincipal claimsPrincipal, CancellationToken cancellationToken = default) =>
        await ExecuteQueryAsync(() => mediator.Send(new GetGoogleLightSearchQuery(mapper.Map<GetGoogleLightSearchRequestDTO>(request), claimsPrincipal.GetUsername(), request.TripVaultName), cancellationToken), mapper.Map<GetGoogleLightSearchResponse>);

    [GraphQLName("hotels")]
    public async Task<GetHotelsResponse> GetHotel([GraphQLName("request")] GetHotelRequest request, [Service] IMediator mediator, [Service] IMapper mapper, ClaimsPrincipal claimsPrincipal, CancellationToken cancellationToken = default) =>
        await ExecuteQueryAsync(() => mediator.Send(new GetHotelsQuery(mapper.Map<GetHotelRequestDTO>(request), claimsPrincipal.GetUsername(), request.TripVaultName), cancellationToken), mapper.Map<GetHotelsResponse>);

    [GraphQLName("localPlaces")]
    public async Task<GetLocalPlacesResponse> GetLocalPlaces([GraphQLName("request")] GetLocalPlacesRequest request, [Service] IMediator mediator, [Service] IMapper mapper, ClaimsPrincipal claimsPrincipal, CancellationToken cancellationToken = default) =>
        await ExecuteQueryAsync(() => mediator.Send(new GetLocalPlacesQuery(mapper.Map<GetLocalPlacesRequestDTO>(request), claimsPrincipal.GetUsername(), request.TripVaultName), cancellationToken), mapper.Map<GetLocalPlacesResponse>);

    [GraphQLName("mapsDirections")]
    public async Task<GetMapsDirectionsResponse> GetMapsDirections([GraphQLName("request")] GetMapsDirectionsRequest request, [Service] IMediator mediator, [Service] IMapper mapper, ClaimsPrincipal claimsPrincipal, CancellationToken cancellationToken = default) =>
        await ExecuteQueryAsync(() => mediator.Send(new GetMapsDirectionsQuery(mapper.Map<GetMapsDirectionsRequestDTO>(request), claimsPrincipal.GetUsername(), request.TripVaultName), cancellationToken), mapper.Map<GetMapsDirectionsResponse>);

    [GraphQLName("mapsPlaceResults")]
    public async Task<GetMapsPlaceResultsResponse> GetMapsPlaceResults([GraphQLName("request")] GetMapsPlaceResultsRequest request, [Service] IMediator mediator, [Service] IMapper mapper, ClaimsPrincipal claimsPrincipal, CancellationToken cancellationToken = default) =>
        await ExecuteQueryAsync(() => mediator.Send(new GetMapsPlaceResultsQuery(mapper.Map<GetMapsPlaceResultsRequestDTO>(request), claimsPrincipal.GetUsername(), request.TripVaultName), cancellationToken), mapper.Map<GetMapsPlaceResultsResponse>);

    [GraphQLName("maps")]
    public async Task<GetMapsResponse> GetMaps([GraphQLName("request")] GetMapsRequest request, [Service] IMediator mediator, [Service] IMapper mapper, ClaimsPrincipal claimsPrincipal, CancellationToken cancellationToken = default) =>
        await ExecuteQueryAsync(() => mediator.Send(new GetMapsQuery(mapper.Map<GetMapsRequestDTO>(request), claimsPrincipal.GetUsername(), request.TripVaultName), cancellationToken), mapper.Map<GetMapsResponse>);

    [GraphQLName("openTableReviews")]
    public async Task<GetOpenTableReviewsResponse> GetOpenTableReviews([GraphQLName("request")] GetOpenTableReviewsRequest request, [Service] IMediator mediator, [Service] IMapper mapper, ClaimsPrincipal claimsPrincipal, CancellationToken cancellationToken = default) =>
        await ExecuteQueryAsync(() => mediator.Send(new GetOpenTableReviewsQuery( mapper.Map<GetOpenTableReviewsRequestDTO>(request), claimsPrincipal.GetUsername(), request.TripVaultName), cancellationToken), mapper.Map<GetOpenTableReviewsResponse>);

    [GraphQLName("placeReviews")]
    public async Task<GetPlaceReviewsResponse> GetPlaceReviews([GraphQLName("request")] GetPlaceReviewsRequest request, [Service] IMediator mediator, [Service] IMapper mapper, ClaimsPrincipal claimsPrincipal, CancellationToken cancellationToken = default) =>
        await ExecuteQueryAsync(() => mediator.Send(new GetPlaceReviewsQuery(mapper.Map<GetPlaceReviewsRequestDTO>(request), claimsPrincipal.GetUsername(), request.TripVaultName), cancellationToken), mapper.Map<GetPlaceReviewsResponse>);

    [GraphQLName("tripAdvisorPlace")]
    public async Task<GetTripAdvisorPlaceResponse> GetTripAdvisorPlace([GraphQLName("request")] GetTripAdvisorPlaceRequest request, [Service] IMediator mediator, [Service] IMapper mapper, ClaimsPrincipal claimsPrincipal, CancellationToken cancellationToken = default) =>
        await ExecuteQueryAsync(() => mediator.Send(new GetTripAdvisorPlaceQuery(mapper.Map<GetTripAdvisorPlaceRequestDTO>(request), claimsPrincipal.GetUsername(), request.TripVaultName), cancellationToken), mapper.Map<GetTripAdvisorPlaceResponse>);

    [GraphQLName("tripAdvisorSearch")]
    public async Task<GetTripAdvisorSearchResponse> GetTripAdvisorSearch([GraphQLName("request")] GetTripAdvisorSearchRequest request, [Service] IMediator mediator, [Service] IMapper mapper, ClaimsPrincipal claimsPrincipal, CancellationToken cancellationToken = default) =>
        await ExecuteQueryAsync(() => mediator.Send(new GetTripAdvisorSearchQuery(mapper.Map<GetTripAdvisorSearchRequestDTO>(request), claimsPrincipal.GetUsername(), request.TripVaultName), cancellationToken), mapper.Map<GetTripAdvisorSearchResponse>);

    [GraphQLName("yelpPlaceFullMenu")]
    public async Task<GetYelpPlaceFullMenuResponse> GetYelpPlaceFullMenu([GraphQLName("request")] GetYelpPlaceFullMenuRequest request, [Service] IMediator mediator, [Service] IMapper mapper, ClaimsPrincipal claimsPrincipal, CancellationToken cancellationToken = default) =>
        await ExecuteQueryAsync(() => mediator.Send(new GetYelpPlaceFullMenuQuery(mapper.Map<GetYelpPlaceFullMenuRequestDTO>(request), claimsPrincipal.GetUsername(), request.TripVaultName), cancellationToken), mapper.Map<GetYelpPlaceFullMenuResponse>);

    [GraphQLName("yelpPlace")]
    public async Task<GetYelpPlaceResponse> GetYelpPlace([GraphQLName("request")] GetYelpPlaceRequest request, [Service] IMediator mediator, [Service] IMapper mapper, ClaimsPrincipal claimsPrincipal, CancellationToken cancellationToken = default) =>
        await ExecuteQueryAsync(() => mediator.Send(new GetYelpPlaceQuery(mapper.Map<GetYelpPlaceRequestDTO>(request), claimsPrincipal.GetUsername(), request.TripVaultName), cancellationToken), mapper.Map<GetYelpPlaceResponse>);

    [GraphQLName("yelpReviews")]
    public async Task<GetYelpReviewsResponse> GetYelpReviews([GraphQLName("request")] GetYelpReviewsRequest request, [Service] IMediator mediator, [Service] IMapper mapper, ClaimsPrincipal claimsPrincipal, CancellationToken cancellationToken = default) =>
        await ExecuteQueryAsync(() => mediator.Send(new GetYelpReviewsQuery( mapper.Map<GetYelpReviewsRequestDTO>(request),  claimsPrincipal.GetUsername(), request.TripVaultName), cancellationToken), mapper.Map<GetYelpReviewsResponse>);

    [GraphQLName("yelpSearch")]
    public async Task<GetYelpSearchResponse> GetYelpSearch([GraphQLName("request")] GetYelpSearchRequest request, [Service] IMediator mediator, [Service] IMapper mapper, ClaimsPrincipal claimsPrincipal, CancellationToken cancellationToken = default) =>
        await ExecuteQueryAsync(() => mediator.Send(new GetYelpSearchQuery(mapper.Map<GetYelpSearchRequestDTO>(request), claimsPrincipal.GetUsername(), request.TripVaultName), cancellationToken), mapper.Map<GetYelpSearchResponse>);

    [GraphQLName("youtubeSearch")]
    public async Task<GetYouTubeSearchResponse> GetYouTubeSearch([GraphQLName("request")] GetYouTubeSearchRequest request, [Service] IMediator mediator, [Service] IMapper mapper, ClaimsPrincipal claimsPrincipal, CancellationToken cancellationToken = default) =>
        await ExecuteQueryAsync(() => mediator.Send(new GetYouTubeSearchQuery(mapper.Map<GetYouTubeSearchRequestDTO>(request), claimsPrincipal.GetUsername(), request.TripVaultName), cancellationToken), mapper.Map<GetYouTubeSearchResponse>);
}
