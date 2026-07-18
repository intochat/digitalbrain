using MediatR;
using TripRadar.Server.Application.Contracts;
using TripRadar.Server.Application.Contracts.Services.Payments;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.Payments.Commands.CreateSetupIntent;

public class CreateSetupIntentHandler(IPaymentService paymentService, ICurrentUserContext currentUserContext)
    : IRequestHandler<CreateSetupIntentCommand, Result<string>>
{
    public async Task<Result<string>> Handle(CreateSetupIntentCommand request, CancellationToken cancellationToken)
    {
        var user = currentUserContext.GetRequiredUser();
        return await paymentService.CreateSetupIntentAsync(user, cancellationToken);
    }
}
