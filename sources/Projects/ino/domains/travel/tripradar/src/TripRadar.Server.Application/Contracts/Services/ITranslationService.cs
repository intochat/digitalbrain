namespace TripRadar.Server.Application.Contracts.Services;

public interface ITranslationService
{
    Task<string> GetTranslationAsync(string? languageCode, string section, string key, params object[] args);
    Task<string> GetCommonTranslationAsync(string? languageCode, string category, string key);
    Task<IEnumerable<string>> GetFeatureListAsync(string? languageCode, string section);
}
