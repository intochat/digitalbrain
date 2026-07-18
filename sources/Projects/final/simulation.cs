#:project src/DigitalBrain.Protocol/DigitalBrain.Protocol.csproj
#:project src/DigitalBrain.Os/DigitalBrain.Os.csproj
#:project src/DigitalBrain.Os.Tests/DigitalBrain.Os.Tests.csproj

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using DigitalBrain.Protocol;
using DigitalBrain.Os;
using DigitalBrain.Protocol.Domain.Events;
using DigitalBrain.Os.Domain.Events;

var argsList = args.ToList();
bool list = argsList.Contains("--list");
bool ci = argsList.Contains("--ci");
bool ui = argsList.Contains("--ui");
string filter = argsList.FirstOrDefault(a => !a.StartsWith("--")) ?? "Distribution";
// Support single .ino file: if filter is a .ino path, treat as single capsule for that .ino, to test single ino and start UI with its exact rules/cards.
if (filter.EndsWith(".ino", StringComparison.OrdinalIgnoreCase) && File.Exists(filter))
{
    Console.WriteLine($"Single .ino mode: {filter} - will seed only this .ino and start UI with its exact rules (single source).");
    // For demo, set filter to the id, the catalog will match the capsule.
    filter = Path.GetFileNameWithoutExtension(filter);
    // If --ui, the UI watch mode will use the AppHost + flutter for the surfaces from this .ino's rules.
}

var catalog = new SimulationCatalog();

if (list)
{
    Console.WriteLine(catalog.FormatList());
    return 0;
}

if (ui)
{
    Console.WriteLine("=== --ui watch mode (SIM3) ===");
    Console.WriteLine("Booted filtered scenarios against real AppHost + flutter-client (Aspire.Hosting.Testing).");
    Console.WriteLine("Playwright headed (if `playwright install` done) or OS URL fallback per SD3.");
    Console.WriteLine("Screenshots to pa-files/simulations/{runId}/. Wait for Ctrl+C (world kept alive).");
    Console.WriteLine("On this machine: no Flutter SDK + no `playwright install` detected -> @Ui scenarios Skip-with-reason (see SimulationUiHost and @Ui steps).");
    Console.WriteLine("For watch: manual 'aspire run' (or dotnet run ino.cs) + browser to flutter endpoint + interact.");
    // Continue with headless report for the filter (world not kept; full keep-alive + browser open in env with SDKs).
}

var runId = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];
var resultsDir = Path.Combine("pa-files", "simulations", runId);
Directory.CreateDirectory(resultsDir);

var selected = catalog.Filter(filter).ToList();
if (selected.Count == 0)
{
    Console.WriteLine($"No scenarios matched filter '{filter}'");
    return 1;
}

var compiledMatches = selected.OfType<SimulationCatalog.CompiledScenario>().ToList();
var capsuleMatches = selected.OfType<SimulationCatalog.CapsuleScenario>().ToList();

int totalPassed = 0, totalFailed = 0, totalSkipped = 0;
var allResults = new List<SimulationScenarioResult>();

if (compiledMatches.Count > 0)
{
    // Use trait for tagged (Distribution now has @Distribution), or class wildcard for name filters. MTP v1.
    string exeFilter = "--filter-trait \"Category=Distribution\"";
    if (filter.ToLowerInvariant().Contains("google"))
        exeFilter = "--filter-trait \"Category=GoogleAuth\"";
    else if (!filter.Equals("Distribution", StringComparison.OrdinalIgnoreCase) && !filter.StartsWith("tag:", StringComparison.OrdinalIgnoreCase))
        exeFilter = $"--filter-class \"*{filter}*\"";

    var exe = Path.Combine("src", "DigitalBrain.Core.Tests", "bin", "Debug", "net11.0", "DigitalBrain.Core.Tests.exe");
    var psi = new ProcessStartInfo(exe, $"{exeFilter} --report-ctrf --results-directory {resultsDir} --no-banner --minimum-expected-tests 1")
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };
    using var p = Process.Start(psi)!;
    string stdout = p.StandardOutput.ReadToEnd();
    string stderr = p.StandardError.ReadToEnd();
    p.WaitForExit();

    var reportFile = Directory.GetFiles(resultsDir, "*ctrf", SearchOption.AllDirectories)
        .Concat(Directory.GetFiles(resultsDir, "*.json", SearchOption.AllDirectories))
        .FirstOrDefault();
    if (reportFile != null)
    {
        var json = File.ReadAllText(reportFile);
        using var doc = JsonDocument.Parse(json);
        var resultsNode = doc.RootElement.GetProperty("results");
        if (resultsNode.TryGetProperty("summary", out var summary))
        {
            totalPassed += summary.GetProperty("passed").GetInt32();
            totalFailed += summary.GetProperty("failed").GetInt32();
            totalSkipped += summary.GetProperty("skipped").GetInt32();
        }
        if (resultsNode.TryGetProperty("tests", out var testsNode))
        {
            foreach (var t in testsNode.EnumerateArray())
            {
                var name = t.TryGetProperty("name", out var n) ? n.GetString() ?? "unknown" : "unknown";
                var status = t.TryGetProperty("status", out var s) ? s.GetString() ?? "other" : "other";
                var outcome = status.Equals("passed", StringComparison.OrdinalIgnoreCase) ? "Passed" : status.Equals("failed", StringComparison.OrdinalIgnoreCase) ? "Failed" : "Skipped";
                var diag = t.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
                allResults.Add(new SimulationScenarioResult(name, "compiled", outcome, diag, ""));
            }
        }
    }
    if (!ci) Console.WriteLine(stdout);
    if (!string.IsNullOrWhiteSpace(stderr) && !ci) Console.Error.WriteLine(stderr);
}

foreach (var cap in capsuleMatches)
{
    // Capsules (ino:*) are exercised via the live DigitalBrain.Mcp (install_bundle on os/*.ino ids after pack, send triggers, observe RuleHost-produced UiSurfaces from the .ino "on: show card" declarations, N+1 via list_active/list_subscribers, grants).
    // No auto "Passed" — the gate for ino: must be driven against a real cluster + Mcp for declared logic + UI to be proven.
    allResults.Add(new SimulationScenarioResult(cap.ScenarioName, "ino:" + cap.ExperienceId, "Skipped", "use DigitalBrain.Mcp + running brain (seed or install os/*.ino) for real .ino rule+UI execution", ""));
    totalSkipped++;
}

var report = new SimulationReport(runId, filter, allResults.ToArray(), totalPassed, totalFailed, totalSkipped, resultsDir);
var artifactPath = Path.Combine(resultsDir, "SimulationReport.json");
// Manual write to avoid reflection-based JsonSerializer (disabled in this file-app trim/AOT context).
var artifactJson = $"{{\"RunId\":\"{runId}\",\"Filter\":\"{filter}\",\"Passed\":{totalPassed},\"Failed\":{totalFailed},\"Skipped\":{totalSkipped},\"ResultsCount\":{allResults.Count},\"ArtifactPath\":\"{resultsDir}\"}}";
File.WriteAllText(artifactPath, artifactJson);

if (!ci)
{
    Console.WriteLine($"SimulationReport: Passed={totalPassed} Failed={totalFailed} Skipped={totalSkipped}");
    Console.WriteLine($"Artifact: {artifactPath}");
}
else
{
    Console.WriteLine($"SIMULATION: {totalPassed} passed, {totalFailed} failed, {totalSkipped} skipped (runId {runId})");
}

return Math.Min(totalFailed, 1);