using TripRadar.Server.Domain.ReferenceData;

namespace TripRadar.Server.Application.Contracts.Services;

public sealed record UserProfileReferenceDataResolution(int? LanguageId, int? CountryId, Timezone? Timezone);
