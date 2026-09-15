namespace DigitalBrain.Telegram.Aspire.Hosting;

public static class TelegramExecutables
{
    public static string Resolve(string name, string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured)) { return configured; }
        var filenames = OperatingSystem.IsWindows() ? new[] { name + ".exe", name + ".bat", name + ".cmd", name } : [name];
        var directories = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).ToList();
        if (OperatingSystem.IsWindows())
        {
            directories.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), name));
            directories.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), name));
            directories.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Links"));
        }
        if (name == "flutter" && Environment.GetEnvironmentVariable("FLUTTER_ROOT") is { Length: > 0 } flutterRoot)
        {
            directories.Add(Path.Combine(flutterRoot, "bin"));
        }
        foreach (var directory in directories)
        {
            foreach (var file in filenames)
            {
                var candidate = Path.Combine(directory.Trim('"'), file);
                if (File.Exists(candidate)) { return Path.GetFullPath(candidate); }
            }
        }
        throw new InvalidOperationException($"The {name} executable was not found. Install it or configure its command path before enabling Telegram onboarding.");
    }
}
