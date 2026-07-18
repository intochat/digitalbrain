using MediatR;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Comms.Core.Attributes;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.Authentication.Commands.Login;

public record LoginCommand(
    [property: Obfuscated] string UsernameOrEmail,
    [property: Obfuscated] string Password) : IRequest<Result<AuthenticationModel>>;
