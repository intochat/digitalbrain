using System.Text.Json;
using DigitalBrain.Core;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Microsoft.Roslyn;

public sealed class RoslynModuleOptions
{
    // The solution path stays Coding's setting; this grain only reads it. Roslyn cannot reference the Coding module
    // (Coding references Roslyn), so it reads the two shared members from Coding's options JSON.
    private const string CodingModuleName = "CodingModule";

    public string? SolutionPath { get; set; }
    public string WorkspaceKey { get; set; } = "digitalbrain";

    internal static void ReadFromCodingOptions(RoslynModuleOptions options, IConfiguration configuration)
    {
        if (configuration[ModuleOptionsSerialization.OptionsKey(CodingModuleName)] is not { Length: > 0 } json) { return; }
        var shared = JsonSerializer.Deserialize<RoslynModuleOptions>(json)
            ?? throw new InvalidOperationException($"Options for module {CodingModuleName} are JSON null.");
        options.SolutionPath = shared.SolutionPath;
        options.WorkspaceKey = shared.WorkspaceKey;
    }
}
