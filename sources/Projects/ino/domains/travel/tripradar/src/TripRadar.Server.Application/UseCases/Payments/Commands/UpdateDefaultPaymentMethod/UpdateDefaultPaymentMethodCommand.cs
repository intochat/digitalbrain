using MediatR;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Application.DTO.Responses;
using TripRadar.Server.Comms.Core.Attributes;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.Payments.Commands.UpdateDefaultPaymentMethod;

public sealed record UpdateDefaultPaymentMethodCommand(
    string Username,
    [property: Obfuscated]
    string? Brand,
    [property: Obfuscated]
    string Last4,
    [property: Obfuscated]
    int ExpMonth,
    [property: Obfuscated]
    int ExpYear,
    bool SetAsDefault) : IRequest<Result<UpdateDefaultPaymentMethodDto>>;
