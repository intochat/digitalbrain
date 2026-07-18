using TripRadar.Server.Domain.SeedWork;

namespace TripRadar.Server.Domain.Enums;

public class PreferenceDataType(int id, string name) : Enumeration(id, name)
{
    public static readonly PreferenceDataType String = new(1, nameof(String));
    public static readonly PreferenceDataType Integer = new(2, nameof(Integer));
    public static readonly PreferenceDataType Boolean = new(3, nameof(Boolean));
    public static readonly PreferenceDataType Array = new(4, nameof(Array));
    public static readonly PreferenceDataType Object = new(5, nameof(Object));
    public static readonly PreferenceDataType Decimal = new(6, nameof(Decimal));
}
