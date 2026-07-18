using MediatR;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.Authentication.Commands.GetTokenByUsername;

public record GetTokenByUsernameCommand(string Username) : IRequest<Result<AuthenticationModel>>;
