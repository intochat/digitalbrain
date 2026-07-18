using CsvHelper.Configuration;
using TripRadar.Server.Db.Models;

namespace TripRadar.Server.Db.Mappings;

internal sealed class LanguagesCsvMap : ClassMap<Languages>
{
    internal LanguagesCsvMap()
    {
        Map(m => m.LanguageId).Name("id");
        Map(m => m.LanguageCode).Name("language_code");
        Map(m => m.LanguageName).Name("language_name");
        Map(m => m.IsInternal).Name("is_internal");
    }
}
