using MediatR;
using TripRadar.Server.Application.Contracts;
using TripRadar.Server.Application.Contracts.Services.Payments;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.Payments.Commands.UpdatePayAsYouGo;

public class UpdatePayAsYouGoHandler(IPaymentService paymentService, ICurrentUserContext currentUserContext)
    : IRequestHandler<UpdatePayAsYouGoCommand, Result>
{
    public async Task<Result> Handle(UpdatePayAsYouGoCommand request, CancellationToken cancellationToken)
    {
        var user = currentUserContext.GetRequiredUser();
        return await paymentService.UpdatePayAsYouGoAsync(user, request.Enabled, cancellationToken);
    }
}
