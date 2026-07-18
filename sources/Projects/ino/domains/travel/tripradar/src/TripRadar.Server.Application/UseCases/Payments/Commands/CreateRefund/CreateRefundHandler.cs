using MediatR;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services.Payments;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.Payments.Commands.CreateRefund;

public class CreateRefundHandler(IPaymentService paymentService, IUnitOfWork unitOfWork, ICurrentUserContext currentUserContext)
    : IRequestHandler<CreateRefundCommand, Result<RefundResult>>
{
    public async Task<Result<RefundResult>> Handle(CreateRefundCommand request, CancellationToken cancellationToken)
    {
        await using var scope = await unitOfWork.StartScopeAsync(cancellationToken: cancellationToken);

        try
        {
            var user = currentUserContext.GetRequiredUser();

            var result = await paymentService.CreateRefundAsync(user, request.Type, request.Metadata, cancellationToken);

            if (result.IsSuccess)
            {
                await scope.CommitAsync(cancellationToken);
            }

            return result;
        }
        catch (Exception)
        {
            return Result.Failure<RefundResult>(Errors.PaymentProcessingFailed);
        }
    }
}
