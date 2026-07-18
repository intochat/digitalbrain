using MediatR;
using TripRadar.Server.Application.Contracts;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.DTO.Responses;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.Users.Commands.UpdateUserProfile;

public sealed class UpdateUserProfileCommandHandler(
    IUnitOfWork unitOfWork,
    IScheduledExecutionRepository scheduledExecutionRepository,
    ICurrentUserContext currentUserContext,
    IRecurringJobService recurringJobService,
    IUserProfileReferenceDataResolver referenceDataResolver,
    IUserProfileAssembler userProfileAssembler)
    : IRequestHandler<UpdateUserProfileCommand, Result<GetUserProfileResponseDTO>>
{
    public async Task<Result<GetUserProfileResponseDTO>> Handle(UpdateUserProfileCommand request, CancellationToken cancellationToken)
    {
        var user = currentUserContext.GetRequiredUser();
        var referenceDataResult = await referenceDataResolver.ResolveAsync(
            request.LanguageCode,
            request.CountryCode,
            request.TimezoneId,
            cancellationToken);

        if (referenceDataResult.IsFailure)
        {
            return Result.Failure<GetUserProfileResponseDTO>(referenceDataResult.Error);
        }

        var referenceData = referenceDataResult.Value;
        var timezoneChanged = request.TimezoneId.HasValue && user.Profile.TimezoneId != request.TimezoneId.Value;

        if (HasProfileUpdates(request, referenceData))
        {
            user.UpdateProfile(
                request.FirstName,
                request.LastName,
                request.PhoneNumber,
                request.TimezoneId,
                request.ProfilePictureUrl,
                referenceData.LanguageId,
                referenceData.CountryId);
        }

        if (request.AllowsMarketingEmails.HasValue)
        {
            user.UpdateMarketingEmailPermission(request.AllowsMarketingEmails.Value);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (timezoneChanged)
        {
            var activeScheduledExecutions = await scheduledExecutionRepository.GetActiveByUserIdAsync(user.Id, cancellationToken);
            var timezoneCodeForScheduling = referenceData.Timezone?.TimezoneCode ?? user.Profile.TimezoneCode;

            foreach (var activeScheduledExecution in activeScheduledExecutions)
            {
                recurringJobService.ScheduleRecurringExecution(
                    activeScheduledExecution.UniqueId,
                    activeScheduledExecution.Schedule,
                    timezoneCodeForScheduling,
                    cancellationToken);
            }
        }

        return Result.Success(userProfileAssembler.Assemble(user));
    }

    private static bool HasProfileUpdates(UpdateUserProfileCommand request, UserProfileReferenceDataResolution referenceData)
    {
        return request.FirstName is not null
               || request.LastName is not null
               || request.PhoneNumber is not null
               || request.TimezoneId.HasValue
               || request.ProfilePictureUrl is not null
               || referenceData.LanguageId.HasValue
               || referenceData.CountryId.HasValue;
    }
}
