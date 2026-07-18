using MediatR;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Comms.Core.Attributes;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.Authentication.Commands.GoogleLogin;

public record GoogleLoginCommand(
    [property: Obfuscated] string Email,
    [property: Obfuscated] string FirstName,
    [property: Obfuscated] string LastName,
    string GoogleId,
    string? ProfilePictureUrl) : IRequest<Result<AuthenticationModel>>;
