using Microsoft.Extensions.AI;

namespace DigitalBrain.CustomerResearcher;

internal static class ResearchHostNames
{
    public static string TrimStartWww(this string host) => host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
}
