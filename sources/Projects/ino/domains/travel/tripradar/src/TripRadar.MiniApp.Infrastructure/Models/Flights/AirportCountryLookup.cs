namespace TripRadar.MiniApp.Client.Infrastructure.Models.Flights;

public static class AirportCountryLookup
{
    private static readonly Dictionary<string, string> Countries = new(StringComparer.OrdinalIgnoreCase)
    {
        // Middle East
        ["DXB"] = "AE", ["AUH"] = "AE", ["SHJ"] = "AE",
        ["DOH"] = "QA",
        ["IST"] = "TR", ["SAW"] = "TR", ["ESB"] = "TR", ["AYT"] = "TR",
        ["TLV"] = "IL",
        ["AMM"] = "JO",
        ["BAH"] = "BH",
        ["KWI"] = "KW",
        ["MCT"] = "OM",
        ["RUH"] = "SA", ["JED"] = "SA",

        // Asia
        ["DEL"] = "IN", ["BOM"] = "IN", ["BLR"] = "IN", ["MAA"] = "IN", ["CCU"] = "IN", ["HYD"] = "IN", ["COK"] = "IN",
        ["HND"] = "JP", ["NRT"] = "JP", ["KIX"] = "JP",
        ["ICN"] = "KR", ["GMP"] = "KR",
        ["PEK"] = "CN", ["PVG"] = "CN", ["CAN"] = "CN", ["CTU"] = "CN", ["HKG"] = "HK",
        ["SIN"] = "SG",
        ["BKK"] = "TH", ["DMK"] = "TH",
        ["KUL"] = "MY",
        ["CGK"] = "ID", ["DPS"] = "ID",
        ["MNL"] = "PH",
        ["SGN"] = "VN", ["HAN"] = "VN",
        ["TPE"] = "TW",
        ["CMB"] = "LK",
        ["DAC"] = "BD",
        ["KTM"] = "NP",
        ["RGN"] = "MM",
        ["PNH"] = "KH",

        // Europe
        ["LHR"] = "GB", ["LGW"] = "GB", ["STN"] = "GB", ["LTN"] = "GB", ["MAN"] = "GB", ["EDI"] = "GB", ["BHX"] = "GB",
        ["CDG"] = "FR", ["ORY"] = "FR",
        ["FRA"] = "DE", ["MUC"] = "DE", ["TXL"] = "DE", ["BER"] = "DE", ["DUS"] = "DE", ["HAM"] = "DE",
        ["AMS"] = "NL",
        ["MAD"] = "ES", ["BCN"] = "ES",
        ["FCO"] = "IT", ["MXP"] = "IT", ["LIN"] = "IT",
        ["ZRH"] = "CH", ["GVA"] = "CH",
        ["VIE"] = "AT",
        ["BRU"] = "BE",
        ["LIS"] = "PT", ["OPO"] = "PT",
        ["ATH"] = "GR",
        ["WAW"] = "PL", ["KRK"] = "PL",
        ["PRG"] = "CZ",
        ["BUD"] = "HU",
        ["OTP"] = "RO",
        ["SOF"] = "BG",
        ["BEG"] = "RS",
        ["ZAG"] = "HR",
        ["CPH"] = "DK",
        ["OSL"] = "NO",
        ["ARN"] = "SE",
        ["HEL"] = "FI",
        ["DUB"] = "IE",
        ["KEF"] = "IS",
        ["RIX"] = "LV",
        ["TLL"] = "EE",
        ["VNO"] = "LT",

        // Americas
        ["JFK"] = "US", ["LAX"] = "US", ["ORD"] = "US", ["SFO"] = "US", ["MIA"] = "US", ["ATL"] = "US", ["DFW"] = "US", ["EWR"] = "US", ["IAD"] = "US", ["SEA"] = "US", ["BOS"] = "US", ["DEN"] = "US",
        ["YYZ"] = "CA", ["YVR"] = "CA", ["YUL"] = "CA",
        ["MEX"] = "MX", ["CUN"] = "MX",
        ["GRU"] = "BR", ["GIG"] = "BR",
        ["EZE"] = "AR",
        ["BOG"] = "CO",
        ["SCL"] = "CL",
        ["LIM"] = "PE",
        ["PTY"] = "PA",

        // Africa
        ["CAI"] = "EG",
        ["JNB"] = "ZA", ["CPT"] = "ZA",
        ["ADD"] = "ET",
        ["NBO"] = "KE",
        ["CMN"] = "MA",
        ["LOS"] = "NG",
        ["DSS"] = "SN",
        ["DAR"] = "TZ",

        // Oceania
        ["SYD"] = "AU", ["MEL"] = "AU", ["BNE"] = "AU",
        ["AKL"] = "NZ",

        // Central Asia / Caucasus
        ["TAS"] = "UZ",
        ["ALA"] = "KZ", ["NQZ"] = "KZ",
        ["TBS"] = "GE",
        ["GYD"] = "AZ",

        // Russia / CIS
        ["SVO"] = "RU", ["DME"] = "RU", ["VKO"] = "RU", ["LED"] = "RU", ["KZN"] = "RU", ["OVB"] = "RU", ["AER"] = "RU", ["SVX"] = "RU",
        ["MSQ"] = "BY",
    };

    public static string? GetCountry(string? iataCode) =>
        iataCode is not null && Countries.TryGetValue(iataCode, out var country) ? country : null;
}