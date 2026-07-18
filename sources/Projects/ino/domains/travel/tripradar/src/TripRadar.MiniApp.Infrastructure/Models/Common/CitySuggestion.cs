namespace TripRadar.MiniApp.Client.Infrastructure.Models.Common
{
    public sealed record CitySuggestion(string City, string CountryCode, List<AirportSuggestion> Airports)
    {
        public string Codes => string.Join(",", Airports.Select(a => a.Code));
        public string Flag => CountryCodeToFlag(CountryCode);
        public bool IsMultiAirport => Airports.Count > 1;

        public static string CountryCodeToFlag(string code)
        {
            if (string.IsNullOrEmpty(code) || code.Length != 2) return "";
            var upper = code.ToUpperInvariant();
            return string.Concat(upper.Select(c => char.ConvertFromUtf32(0x1F1E6 + c - 'A')));
        }

        public static int? ComputeDistanceKm(double? lat1, double? lon1, double? lat2, double? lon2)
        {
            if (lat1 is null || lon1 is null || lat2 is null || lon2 is null) return null;
            const double r = 6371;
            var dLat = (lat2.Value - lat1.Value) * Math.PI / 180;
            var dLon = (lon2.Value - lon1.Value) * Math.PI / 180;
            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                    Math.Cos(lat1.Value * Math.PI / 180) * Math.Cos(lat2.Value * Math.PI / 180) *
                    Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return (int)Math.Round(r * c);
        }
    }
}