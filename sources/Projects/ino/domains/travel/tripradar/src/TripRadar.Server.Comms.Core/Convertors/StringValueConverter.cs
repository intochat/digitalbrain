using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using System.Text.Json;

namespace TripRadar.Server.Comms.Core.Convertors;

public class StringValueConverter<TProp, TImpl>() :
    ValueConverter<TProp, string>(m => ConvertToString(m), s => ConvertFromString(s))
    where TImpl : TProp, new()
{
    private static string ConvertToString(TProp value) => JsonSerializer.Serialize(value, options: new JsonSerializerOptions { WriteIndented = false });

    private static TProp ConvertFromString(string value) => JsonSerializer.Deserialize<TImpl>(value, options: new JsonSerializerOptions { PropertyNameCaseInsensitive = false }) ?? new TImpl();
}
