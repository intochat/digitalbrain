using DigitalBrain.Core;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Microsoft.CSharp;

internal static class CSharpAuthoringGate
{
    public static bool IsOpen(IConfiguration configuration) => DeveloperMode.IsEnabled(configuration);
}
