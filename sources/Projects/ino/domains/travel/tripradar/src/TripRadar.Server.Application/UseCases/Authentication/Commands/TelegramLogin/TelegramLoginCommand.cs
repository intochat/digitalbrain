using MediatR;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.Authentication.Commands.TelegramLogin;

public record TelegramLoginCommand(TelegramAuthDataDTO AuthData) : IRequest<Result<AuthenticationModel>>;
