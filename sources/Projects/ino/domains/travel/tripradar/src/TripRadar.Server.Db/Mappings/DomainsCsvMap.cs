using CsvHelper.Configuration;
using TripRadar.Server.Db.Models;

namespace TripRadar.Server.Db.Mappings;

internal sealed class DomainsCsvMap : ClassMap<Domains>
{
    internal DomainsCsvMap()
    {
        Map(m => m.DomainId).Name("id");
        Map(m => m.Domain).Name("domain");
        Map(m => m.LanguageCode).Name("language_code");
        Map(m => m.CountryCode).Name("country_code");
        Map(m => m.CountryName).Name("country_name");
    }
}
