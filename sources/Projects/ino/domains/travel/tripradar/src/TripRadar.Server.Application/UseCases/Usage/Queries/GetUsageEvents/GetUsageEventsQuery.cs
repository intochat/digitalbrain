using MediatR;
using TripRadar.Server.Application.Contracts.Requests;
using TripRadar.Server.Application.DTO.Responses;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.Usage.Queries.GetUsageEvents;

public sealed record GetUsageEventsQuery(string Username, DateOnly? From = null, DateOnly? To = null, string GroupBy = "day", string? ServiceType = null, Guid? TripVaultUniqueId = null, string? Source = null, int Page = 1, int PageSize = 20) : IRequest<Result<GetUsageEventsResponseDTO>>, IAuthorizedRequest;
