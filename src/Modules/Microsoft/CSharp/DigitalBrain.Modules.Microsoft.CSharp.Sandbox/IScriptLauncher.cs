namespace DigitalBrain.Microsoft.CSharp.Sandbox;

internal interface IScriptLauncher
{
    IScriptProcess Start(string workDirectory, IReadOnlyDictionary<string, string> environment, Action<string> output);
}

internal interface IScriptProcess
{
    Task<int> Completion { get; }

    Task StopAsync(TimeSpan grace);
}
