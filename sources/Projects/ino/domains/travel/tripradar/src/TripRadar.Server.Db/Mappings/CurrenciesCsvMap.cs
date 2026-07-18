using CsvHelper.Configuration;
using TripRadar.Server.Db.Models;

namespace TripRadar.Server.Db.Mappings;

internal sealed class CurrenciesCsvMap : ClassMap<Currencies>
{
    internal CurrenciesCsvMap()
    {
        Map(m => m.CurrencyId).Name("id");
        Map(m => m.CurrencyCode).Name("currency_code");
        Map(m => m.CurrencyName).Name("currency_name");
    }
}
