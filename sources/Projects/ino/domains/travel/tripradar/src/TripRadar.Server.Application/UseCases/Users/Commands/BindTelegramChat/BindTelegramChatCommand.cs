using MediatR;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.Users.Commands.BindTelegramChat;

public record BindTelegramChatCommand(string Username, long TelegramUserId) : IRequest<Result>;
