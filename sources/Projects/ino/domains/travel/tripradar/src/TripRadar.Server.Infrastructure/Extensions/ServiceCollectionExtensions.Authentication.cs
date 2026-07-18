using Microsoft.Extensions.DependencyInjection;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.Contracts.Services.Authentication;
using TripRadar.Server.Infrastructure.Contracts.Authentication;
using TripRadar.Server.Infrastructure.Services;
using TripRadar.Server.Infrastructure.Services.Authentication;

namespace TripRadar.Server.Infrastructure.Extensions;

public static partial class ServiceCollectionExtensions
{
    private static IServiceCollection ConfigureAuthenticationInfrastructure(this IServiceCollection services) =>
        services
            .AddScoped<IAuthenticationService, AuthenticationService>()
            .AddScoped<IGoogleAuthenticationService, GoogleAuthenticationService>()
            .AddScoped<ITelegramAuthValidationService, TelegramAuthValidationService>()
            .AddScoped<ITelegramInitDataParser, TelegramInitDataParser>()
            .AddScoped<IAuthenticationTokenIssuer, AuthenticationTokenIssuer>()
            .AddScoped<IGoogleAuthenticationOrchestrator, GoogleAuthenticationOrchestrator>()
            .AddScoped<IPasswordVerificationService, PasswordVerificationService>()
            .AddScoped<IUserAuthenticationValidator, UserAuthenticationValidator>()
            .AddScoped<ICredentialValidator, CredentialValidator>()
            .AddScoped<IUserLookupService, UserLookupService>()
            .AddScoped<ITelegramAuthenticationService, TelegramAuthenticationService>();
}
