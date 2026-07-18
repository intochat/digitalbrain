namespace TripRadar.Server.Application.Contracts.Services;

public interface ILanguageResolver
{
    Task<string> ResolveLanguageAsync(string? languageCode, CancellationToken cancellationToken = default);
}
