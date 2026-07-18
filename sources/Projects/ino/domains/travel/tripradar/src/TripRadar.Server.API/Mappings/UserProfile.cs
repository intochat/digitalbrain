using AutoMapper;
using TripRadar.Server.API.Contracts.Requests.Create;
using TripRadar.Server.API.Contracts.Requests.Update;
using TripRadar.Server.API.Contracts.Responses.Get;
using TripRadar.Server.API.Contracts.Responses.Update;
using TripRadar.Server.Application.DTO.Responses;
using TripRadar.Server.Application.UseCases.Users.Commands.ChangePassword;
using TripRadar.Server.Application.UseCases.Users.Commands.CreateNewUser;
using TripRadar.Server.Application.UseCases.Users.Commands.ForgotPassword;
using TripRadar.Server.Application.UseCases.Users.Commands.ResendEmailConfirmation;
using TripRadar.Server.Application.UseCases.Users.Commands.ResetPassword;
using TripRadar.Server.Application.UseCases.Users.Commands.UpdateUserProfile;
using TripRadar.Server.Domain.Aggregates;

namespace TripRadar.Server.API.Mappings;

internal sealed class UserProfile : Profile
{
    public UserProfile()
    {
        CreateMap<CreateUserRequest, CreateNewUserCommand>()
            .ConstructUsing(src => new CreateNewUserCommand(
                src.Password,
                src.Email,
                src.FirstName,
                src.LastName,
                src.PhoneNumber,
                src.HasDataStorageConsent,
                null));
        CreateMap<CreateNewUserCommand, User>();
        CreateMap<ForgotPasswordRequest, ForgotPasswordCommand>();
        CreateMap<ResetPasswordRequest, ResetPasswordCommand>();
        CreateMap<ChangePasswordRequest, ChangePasswordCommand>()
            .ConstructUsing((src, _) => new ChangePasswordCommand(
                Username: string.Empty,
                CurrentPassword: src.CurrentPassword,
                NewPassword: src.NewPassword));
        CreateMap<ResendEmailConfirmationRequest, ResendEmailConfirmationCommand>();
        CreateMap<UpdateUserProfileRequest, UpdateUserProfileCommand>()
            .ConstructUsing((src, _) => new UpdateUserProfileCommand(
                Username: string.Empty,
                FirstName: src.FirstName,
                LastName: src.LastName,
                PhoneNumber: src.PhoneNumber,
                TimezoneId: src.TimezoneId,
                ProfilePictureUrl: src.ProfilePictureUrl,
                LanguageCode: src.LanguageCode,
                CountryCode: src.CountryCode,
                AllowsMarketingEmails: src.AllowsMarketingEmails
            ));

        CreateMap<GetUserProfileResponseDTO, GetUserProfileResponse>()
            .ForMember(dest => dest.LanguageCode, opt => opt.MapFrom(src => src.LanguageCode))
            .ForMember(dest => dest.LanguageName, opt => opt.MapFrom(src => src.LanguageName))
            .ForMember(dest => dest.CountryCode, opt => opt.MapFrom(src => src.CountryCode))
            .ForMember(dest => dest.CountryName, opt => opt.MapFrom(src => src.CountryName))
            .ForMember(dest => dest.AllowsMarketingEmails, opt => opt.MapFrom(src => src.AllowsMarketingEmails));
        CreateMap<GetUserProfileResponseDTO, UpdateUserProfileResponse>()
            .ForMember(dest => dest.LanguageCode, opt => opt.MapFrom(src => src.LanguageCode))
            .ForMember(dest => dest.LanguageName, opt => opt.MapFrom(src => src.LanguageName))
            .ForMember(dest => dest.CountryCode, opt => opt.MapFrom(src => src.CountryCode))
            .ForMember(dest => dest.CountryName, opt => opt.MapFrom(src => src.CountryName))
            .ForMember(dest => dest.AllowsMarketingEmails, opt => opt.MapFrom(src => src.AllowsMarketingEmails));
    }
}
