using CsvHelper.Configuration;
using TripRadar.Server.Db.Models;

namespace TripRadar.Server.Db.Mappings;

internal sealed class AirportsCsvMap : ClassMap<Airports>
{
    internal AirportsCsvMap()
    {
        Map(m => m.Code).Name("iata_code");
        Map(m => m.Name).Name("name");
        Map(m => m.City).Name("municipality");
        Map(m => m.Country).Name("iso_country");
        Map(m => m.Latitude).Name("latitude_deg").Optional();
        Map(m => m.Longitude).Name("longitude_deg").Optional();
        Map(m => m.AirportType).Name("type").Optional();
    }
}
