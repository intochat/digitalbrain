namespace DigitalBrain.Coding;

public sealed record ProcessResult(int ExitCode, string Output, string Error, TimeSpan Duration, bool TimedOut);