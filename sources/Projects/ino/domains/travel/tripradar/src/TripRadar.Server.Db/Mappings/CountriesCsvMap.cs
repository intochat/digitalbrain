using CsvHelper.Configuration;
using TripRadar.Server.Db.Models;

namespace TripRadar.Server.Db.Mappings;

internal sealed class CountriesCsvMap : ClassMap<Countries>
{
    internal CountriesCsvMap()
    {
        Map(m => m.CountryId).Name("id");
        Map(m => m.CountryCode).Name("country_code");
        Map(m => m.CountryName).Name("country_name");
    }
}
