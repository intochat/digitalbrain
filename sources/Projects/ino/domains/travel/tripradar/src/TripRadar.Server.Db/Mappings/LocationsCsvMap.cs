using CsvHelper.Configuration;
using TripRadar.Server.Db.Models;

namespace TripRadar.Server.Db.Mappings;

internal sealed class LocationsCsvMap : ClassMap<Locations>
{
    internal LocationsCsvMap()
    {
        Map(m => m.LocationId).Name("id");
        Map(m => m.RowId).Name("row_id");
        Map(m => m.GoogleId).Name("google_id");
        Map(m => m.GoogleParentId).Name("google_parent_id");
        Map(m => m.Name).Name("name");
        Map(m => m.CanonicalName).Name("canonical_name");
        Map(m => m.CountryCode).Name("country_code");
        Map(m => m.TargetType).Name("target_type");
        Map(m => m.Reach).Name("reach");
        Map(m => m.GpsLongitude).Name("gps_longitude");
        Map(m => m.GpsLatitude).Name("gps_latitude");
    }
}
