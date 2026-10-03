using System.Text.Json;

namespace DigitalBrain.Apps;

// Script-side test support: every check owns a fresh installation and reports a stable scenario ID.
public sealed class AppScenarioSuite(
    Func<string, IApp> apps,
    PackageRevisionRef revision,
    TextWriter? output = null,
    Func<string, IReadOnlyDictionary<string, string>>? settings = null,
    string? brainScope = null)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HashSet<string> reported = new(StringComparer.Ordinal);
    private int failures;
    public int ExitCode => failures == 0 && reported.Count > 0 ? 0 : 1;

    public async Task Run(string id, string name, Func<IApp, string, Task> check, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!reported.Add(id)) { throw new ArgumentException("Each scenario ID may run only once.", nameof(id)); }
        var scope = (brainScope is null ? "" : brainScope + "/") + $"specs/{revision.Package}@{revision.Revision}/{Guid.NewGuid():N}";
        var app = apps(scope + "/app");
        string? error = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await app.Install(new(Guid.NewGuid(), revision, settings?.Invoke(scope) ?? new Dictionary<string, string>()));
            await check(app, scope);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (Exception failure) { error = failure.Message; }
        finally
        {
            try { await app.Uninstall(new(Guid.NewGuid())); }
            catch (Exception cleanup) { error = (error is null ? "" : error + " ") + "Cleanup failed: " + cleanup.Message; }
        }
        if (error is not null) { failures++; }
        await (output ?? Console.Out).WriteLineAsync("dbtest:json " + JsonSerializer.Serialize(new AppScenarioVerdict(name, error is null, error ?? "", id), Json));
        cancellationToken.ThrowIfCancellationRequested();
    }
}
