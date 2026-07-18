using MediatR;
using TripRadar.Server.Application.Contracts.Requests;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Application.DTO.Responses;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.Payments.Queries.GetInvoices;

public sealed record GetInvoicesQuery(
    string Username,
    int Limit = 20,
    string? StartingAfter = null,
    string? Status = null) : IRequest<Result<InvoicesDTO>>, IAuthorizedRequest;
