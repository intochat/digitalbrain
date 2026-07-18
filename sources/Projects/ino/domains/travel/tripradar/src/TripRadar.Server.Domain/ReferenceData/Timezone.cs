namespace TripRadar.Server.Domain.ReferenceData;

public class Timezone
{
    private Timezone()
    {
    }

    public Timezone(int timezoneId, string timezoneCode, string timezoneName)
    {
        TimezoneId = timezoneId;
        TimezoneCode = timezoneCode.Trim();
        TimezoneName = timezoneName.Trim();
    }

    public int TimezoneId { get; }

    public string TimezoneCode { get; } = null!;

    public string TimezoneName { get; } = null!;
}
