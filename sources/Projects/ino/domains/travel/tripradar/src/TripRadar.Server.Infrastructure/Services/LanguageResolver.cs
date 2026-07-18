using System.Collections.Concurrent;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Infrastructure.Constants;

namespace TripRadar.Server.Infrastructure.Services;

public class LanguageResolver(ILanguageRepository languageRepository) : ILanguageResolver
{
    private readonly ILanguageRepository _languageRepository = languageRepository ?? throw new ArgumentNullException(nameof(languageRepository));
    private readonly ConcurrentDictionary<string, string> _languageCache = new();
    private readonly SemaphoreSlim _cacheSemaphore = new(1, 1);

    public async Task<string> ResolveLanguageAsync(string? languageCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return EmailConstants.DefaultLanguage;
        }

        var normalized = languageCode.Trim().ToLowerInvariant();

        if (_languageCache.TryGetValue(normalized, out var cachedLanguage))
        {
            return cachedLanguage;
        }

        await _cacheSemaphore.WaitAsync(cancellationToken);
        try
        {
            if (_languageCache.TryGetValue(normalized, out cachedLanguage))
            {
                return cachedLanguage;
            }

            var resolvedLanguage = await ResolveLanguageFromDatabaseAsync(normalized, cancellationToken);
            _languageCache.TryAdd(normalized, resolvedLanguage);
            return resolvedLanguage;
        }
        finally
        {
            _cacheSemaphore.Release();
        }
    }

    private async Task<string> ResolveLanguageFromDatabaseAsync(string languageCode, CancellationToken cancellationToken)
    {
        try
        {
            var language = await _languageRepository.GetByCodeAsync(languageCode, cancellationToken);
            return language is not null ? languageCode : EmailConstants.DefaultLanguage;
        }
        catch (Exception)
        {
            return EmailConstants.DefaultLanguage;
        }
    }
}
