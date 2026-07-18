using MediatR;
using TripRadar.Server.Comms.Core.Attributes;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.Users.Commands.ForgotPassword;

public record ForgotPasswordCommand([property: Obfuscated] string Email) : IRequest<Result>;
