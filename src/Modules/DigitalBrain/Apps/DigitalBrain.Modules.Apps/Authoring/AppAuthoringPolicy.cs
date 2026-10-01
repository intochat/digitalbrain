namespace DigitalBrain.Apps;

internal sealed class AppAuthoringPolicy(IEnumerable<IAppRuntime> runtimes, IScriptSandbox? sandbox = null)
{
    public const string SandboxMissing = "This host has no C# sandbox, and verifying an app runs its tests as one. Compose CSharpModule where the brain can run scripts.";

    public string[] AvailableRuntimes => sandbox?.CanRun == true
        ? [PackageManifest.CSharpRuntime, .. runtimes.Select(runtime => runtime.Name).Distinct(StringComparer.Ordinal)]
        : [.. runtimes.Select(runtime => runtime.Name).Distinct(StringComparer.Ordinal)];

    public void RequireSandbox()
    {
        if (sandbox?.CanRun != true) { throw new InvalidOperationException(SandboxMissing); }
    }

    public void RequireRuntime(string runtime)
    {
        if (runtime == PackageManifest.CSharpRuntime) { RequireSandbox(); }
        if (!AvailableRuntimes.Contains(runtime, StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"This host has no '{runtime}' app runtime.");
        }
    }
}
