using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace DigitalBrain.CSharpExpert;

public sealed class SolutionPolicy(IOptions<CSharpExpertModuleOptions> options, IConfiguration configuration)
{
    private static readonly string[] SolutionExtensions = [".sln", ".slnx", ".csproj"];

    public string Validate(string solutionPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(solutionPath);
        var full = Path.GetFullPath(solutionPath);
        if (!SolutionExtensions.Contains(Path.GetExtension(full), StringComparer.OrdinalIgnoreCase) || !File.Exists(full))
        {
            throw new InvalidOperationException($"'{solutionPath}' is not an existing .sln, .slnx or .csproj file.");
        }

        if (!AllowedRoots().Any(root => IsUnder(full, root)))
        {
            throw new InvalidOperationException($"'{solutionPath}' is outside the folders the C# expert may work in.");
        }

        return full;
    }

    private IEnumerable<string> AllowedRoots()
    {
        foreach (var root in options.Value.AllowedSolutionRoots.Where(root => root.Length > 0))
        {
            yield return Path.GetFullPath(root);
        }

        if (configuration["DigitalBrain:Coding:SolutionPath"] is { Length: > 0 } codingSolution)
        {
            yield return Path.GetDirectoryName(Path.GetFullPath(codingSolution))!;
        }
    }

    public static bool IsUnder(string path, string root)
    {
        var relative = Path.GetRelativePath(root, path);
        return !relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative);
    }
}
