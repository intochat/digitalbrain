namespace TripRadar.Server.Application.Contracts.Services.Emails;

public interface IEmailTemplateGeneratorService
{
    Task<string> GenerateEmailAsync(EmailType emailType, EmailParameters parameters, CancellationToken cancellationToken = default);
}
