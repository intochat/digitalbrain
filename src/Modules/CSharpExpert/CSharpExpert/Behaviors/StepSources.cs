using System.Text;

namespace DigitalBrain.CSharpExpert;

internal static class StepSources
{
    private const int MaxCharactersPerFile = 20_000;

    public static string Read(string? solutionFolder, IReadOnlyList<string> files)
    {
        if (solutionFolder is null)
        {
            return string.Empty;
        }

        var sources = new StringBuilder();
        foreach (var file in files)
        {
            var path = Path.GetFullPath(Path.Combine(solutionFolder, file));
            if (!SolutionPolicy.IsUnder(path, solutionFolder) || !File.Exists(path))
            {
                continue;
            }

            var text = File.ReadAllText(path);
            sources.AppendLine($"// file: {file}");
            sources.AppendLine(text.Length > MaxCharactersPerFile ? text[..MaxCharactersPerFile] : text);
        }

        return sources.ToString();
    }
}
