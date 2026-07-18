using MediatR;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.Authentication.Commands.GetTokenByTelegramUserId;

public record GetTokenByTelegramUserIdCommand(long TelegramUserId) : IRequest<Result<AuthenticationModel>>;
