using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Domain.Enums;
using TripRadar.Server.Infrastructure.Constants;

namespace TripRadar.Server.Infrastructure.Services;

public class TranslationService(ILogger<TranslationService> logger) : ITranslationService
{
    private static readonly TimeSpan _cacheSlidingExpiration = TimeSpan.FromHours(6);
    private static readonly TimeSpan _lockCacheSlidingExpiration = TimeSpan.FromHours(1);
    private const int LanguageLocksCacheMaxSize = 50; // Cap to prevent memory leak - matches reasonable language count

    private readonly ILogger<TranslationService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly MemoryCache _languageCache = new(new MemoryCacheOptions { SizeLimit = 8 });
    // Use MemoryCache instead of ConcurrentDictionary to allow eviction of unused locks
    private readonly MemoryCache _languageLocks = new(new MemoryCacheOptions { SizeLimit = LanguageLocksCacheMaxSize });
    private readonly Lock _lockCreationLock = new();
    private volatile bool _hasLoggedStatus;

    public async Task<string> GetTranslationAsync(string? languageCode, string section, string key, params object[] args)
    {
        LogStatus();

        var resolvedLanguage = await ResolveLanguageAsync(languageCode);
        var translation = await GetTranslationValueAsync(resolvedLanguage, section, key);

        if (string.IsNullOrEmpty(translation))
        {
            return string.Format(CultureInfo.InvariantCulture, EmailConstants.Fallbacks.MissingTranslation, section, key);
        }

        try
        {
            return args.Length > 0 ? string.Format(CultureInfo.InvariantCulture, translation, args) : translation;
        }
        catch (FormatException ex)
        {
            _logger.LogError(ex, "Format error in translation '{Language}.{Section}.{Key}' with {ArgCount} arguments", resolvedLanguage, section, key, args.Length);
            return translation;
        }
    }

    public Task<string> GetCommonTranslationAsync(string? languageCode, string category, string key) =>
        GetTranslationAsync(languageCode, EmailConstants.Sections.Common, $"{category}.{key}");

    public async Task<IEnumerable<string>> GetFeatureListAsync(string? languageCode, string section)
    {
        LogStatus();

        var resolvedLanguage = await ResolveLanguageAsync(languageCode);
        var features = await GetFeatureListInternalAsync(resolvedLanguage, section);

        if (features.Count == 0 && resolvedLanguage != EmailConstants.DefaultLanguage)
            features = await GetFeatureListInternalAsync(EmailConstants.DefaultLanguage, section);

        return features;
    }

    private async Task<string> ResolveLanguageAsync(string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode) || !SupportedLanguageType.GetAllLanguageCodes().Contains(languageCode))
        {
            return EmailConstants.DefaultLanguage;
        }

        var translations = await GetTranslationsForLanguageAsync(languageCode);
        return translations.HasEntries ? languageCode : EmailConstants.DefaultLanguage;
    }

    private async Task<string> GetTranslationValueAsync(string languageCode, string section, string path)
    {
        var translation = await GetTranslationInternalAsync(languageCode, section, path);

        if (string.IsNullOrEmpty(translation) && languageCode != EmailConstants.DefaultLanguage)
        {
            _logger.LogDebug("Falling back to default language for '{Language}.{Section}.{Path}'", languageCode, section, path);
            translation = await GetTranslationInternalAsync(EmailConstants.DefaultLanguage, section, path);
        }

        return translation;
    }

    private async Task<string> GetTranslationInternalAsync(string languageCode, string section, string path)
    {
        var translations = await GetTranslationsForLanguageAsync(languageCode);
        var fullKey = $"{section}.{path}";
        return translations.Strings.TryGetValue(fullKey, out var value) ? value : string.Empty;
    }

    private async Task<IReadOnlyList<string>> GetFeatureListInternalAsync(string languageCode, string section)
    {
        var translations = await GetTranslationsForLanguageAsync(languageCode);
        var fullKey = $"{section}.{EmailConstants.Keys.Features}";
        return translations.Lists.TryGetValue(fullKey, out var value) ? value : [];
    }

    private Task<LanguageTranslations> GetTranslationsForLanguageAsync(string language)
    {
        if (_languageCache.TryGetValue(language, out LanguageTranslations? cached) && cached != null)
        {
            return Task.FromResult(cached);
        }

        return LoadAndCacheLanguageAsync(language);
    }

    private async Task<LanguageTranslations> LoadAndCacheLanguageAsync(string language)
    {
        // Get or create a semaphore for this language with proper eviction
        SemaphoreSlim semaphore;
        lock (_lockCreationLock)
        {
            if (!_languageLocks.TryGetValue(language, out SemaphoreSlim? cached) || cached == null)
            {
                cached = new SemaphoreSlim(1, 1);
                _languageLocks.Set(language, cached, new MemoryCacheEntryOptions
                {
                    Size = 1,
                    SlidingExpiration = _lockCacheSlidingExpiration,
                    // Dispose semaphore when evicted to prevent resource leak
                    PostEvictionCallbacks = { new PostEvictionCallbackRegistration
                    {
                        EvictionCallback = (_, value, _, _) => (value as SemaphoreSlim)?.Dispose()
                    }}
                });
            }
            semaphore = cached;
        }

        await semaphore.WaitAsync();
        try
        {
            if (_languageCache.TryGetValue(language, out LanguageTranslations? cached) && cached != null)
            {
                return cached;
            }

            var translations = await LoadLanguageTranslationsAsync(language);
            _languageCache.Set(language, translations, new MemoryCacheEntryOptions
            {
                Size = 1,
                SlidingExpiration = _cacheSlidingExpiration
            });

            return translations;
        }
        finally
        {
            semaphore.Release();
        }
    }

    private async Task<LanguageTranslations> LoadLanguageTranslationsAsync(string language)
    {
        var translations = new LanguageTranslations(new Dictionary<string, string>(StringComparer.Ordinal), new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal));
        var resourcePath = GetTranslationFilePath(language);

        if (resourcePath == null)
        {
            return translations;
        }

        try
        {
            await using var fileStream = new FileStream(resourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true);
            using var document = await JsonDocument.ParseAsync(fileStream);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
                foreach (var section in document.RootElement.EnumerateObject())
                    AddEntries(section.Name, section.Value, translations);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading translations for language '{Language}'", language);
        }

        return translations;
    }

    private static void AddEntries(string prefix, JsonElement element, LanguageTranslations translations)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var child in element.EnumerateObject())
                    AddEntries($"{prefix}.{child.Name}", child.Value, translations);
                break;
            case JsonValueKind.Array:
                var items = new List<string>();
                foreach (var item in element.EnumerateArray())
                {
                    switch (item.ValueKind)
                    {
                        case JsonValueKind.String:
                            items.Add(item.GetString() ?? string.Empty);
                            break;
                        case JsonValueKind.Number:
                        case JsonValueKind.True:
                        case JsonValueKind.False:
                            items.Add(item.ToString());
                            break;
                    }
                }

                if (items.Count > 0)
                    translations.Lists[prefix] = items;
                break;
            case JsonValueKind.String:
                translations.Strings[prefix] = element.GetString() ?? string.Empty;
                break;
            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
                translations.Strings[prefix] = element.ToString();
                break;
        }
    }

    private static string? GetTranslationFilePath(string language)
    {
        var possiblePaths = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, EmailConstants.ResourcesPath, EmailConstants.LocalizationPath, $"{language}{EmailConstants.JsonFileExtension}"),
            Path.Combine(Directory.GetCurrentDirectory(), EmailConstants.ResourcesPath, EmailConstants.LocalizationPath, $"{language}{EmailConstants.JsonFileExtension}"),
            Path.Combine(AppContext.BaseDirectory, EmailConstants.ResourcesPath, EmailConstants.LocalizationPath, $"{language}{EmailConstants.JsonFileExtension}")
        };

        return possiblePaths.FirstOrDefault(File.Exists);
    }

    private sealed class LanguageTranslations(
        Dictionary<string, string> strings,
        Dictionary<string, IReadOnlyList<string>> lists)
    {
        public Dictionary<string, string> Strings { get; } = strings;
        public Dictionary<string, IReadOnlyList<string>> Lists { get; } = lists;
        public bool HasEntries => Strings.Count > 0 || Lists.Count > 0;
    }

    private void LogStatus()
    {
        if (_hasLoggedStatus)
        {
            return;
        }

        lock (this)
        {
            if (_hasLoggedStatus)
            {
                return;
            }

            var supported = SupportedLanguageType.GetAllLanguageCodes();
            _logger.LogInformation("Translation service initialized with supported languages: {Languages}", string.Join(", ", supported));
            _hasLoggedStatus = true;
        }
    }
}
