using MediatR;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.Users.Commands.UnsubscribeMarketingEmails;

public sealed record UnsubscribeMarketingEmailsCommand(string? Username, string? Email) : IRequest<Result>;
