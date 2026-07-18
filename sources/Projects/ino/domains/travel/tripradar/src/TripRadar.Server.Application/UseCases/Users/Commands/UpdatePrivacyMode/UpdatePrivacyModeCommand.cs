using MediatR;
using TripRadar.Server.Application.Contracts.Requests;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.Users.Commands.UpdatePrivacyMode;

public sealed record UpdatePrivacyModeCommand(string Username, bool Enabled) : IRequest<Result>, IAuthorizedRequest;
