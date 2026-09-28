namespace DigitalBrain.Microsoft.DotNet;

public sealed record ProcessResult(int ExitCode, string Output, string Error, TimeSpan Duration, bool TimedOut);