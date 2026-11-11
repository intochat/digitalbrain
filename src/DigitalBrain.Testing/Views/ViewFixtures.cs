using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using DigitalBrain.UI;
using DigitalBrain.Units;

namespace DigitalBrain.Testing.Views;

/// <summary>Writes and verifies deterministic JSON view fixtures.</summary>
public static class ViewFixtures
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Writes a JSON file for <paramref name="view"/>.</summary>
    public static void Write(PublishedView view, string directory)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, $"{Slug(view.Alias)}.json");
        var json = NormalizeLineEndings(JsonSerializer.Serialize(view.View, JsonOptions));
        File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    /// <summary>Verifies that <paramref name="view"/> matches the fixture in <paramref name="directory"/>, or writes it when <c>DIGITALBRAIN_UPDATE_VIEWS=1</c>.</summary>
    public static void Verify(PublishedView view, string directory)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        if (string.Equals(Environment.GetEnvironmentVariable("DIGITALBRAIN_UPDATE_VIEWS"), "1", StringComparison.Ordinal))
        {
            Write(view, directory);
            return;
        }

        var fileName = $"{Slug(view.Alias)}.json";
        var path = Path.Combine(directory, fileName);
        var actualJson = NormalizeLineEndings(JsonSerializer.Serialize(view.View, JsonOptions));

        if (!File.Exists(path))
        {
            throw new ViewFixtureMismatchException(
                $"Missing view fixture '{path}' for screen '{view.Alias}'. Run with DIGITALBRAIN_UPDATE_VIEWS=1 to write fixtures.");
        }

        var onDiskJson = NormalizeLineEndings(File.ReadAllText(path, Encoding.UTF8));
        if (!string.Equals(onDiskJson, actualJson, StringComparison.Ordinal))
        {
            throw new ViewFixtureMismatchException(
                BuildMismatchMessage(view.Alias, "default", path, onDiskJson, actualJson));
        }
    }

    /// <summary>Converts a scenario title to its lowercase ASCII kebab-case file slug.</summary>
    public static string Slug(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        var lower = title.Trim().ToLowerInvariant();
        var sb = new StringBuilder(lower.Length);
        var previousDash = false;
        foreach (var ch in lower)
        {
            if (char.IsAsciiLetterOrDigit(ch))
            {
                sb.Append(ch);
                previousDash = false;
            }
            else if (!previousDash && sb.Length > 0)
            {
                sb.Append('-');
                previousDash = true;
            }
        }
        if (sb.Length > 0 && sb[^1] == '-')
        {
            sb.Length--;
        }
        return sb.Length == 0 ? "scenario" : sb.ToString();
    }

    private static string NormalizeLineEndings(string text)
        => text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n') + "\n";

    private static string BuildMismatchMessage(string appId, string scenario, string path, string expected, string actual)
    {
        var expectedLines = expected.Split('\n');
        var actualLines = actual.Split('\n');
        var max = Math.Max(expectedLines.Length, actualLines.Length);
        for (var i = 0; i < max; i++)
        {
            var e = i < expectedLines.Length ? expectedLines[i] : "<eof>";
            var a = i < actualLines.Length ? actualLines[i] : "<eof>";
            if (!string.Equals(e, a, StringComparison.Ordinal))
            {
                var diffLine = i + 1;
                return $"View fixture mismatch for app '{appId}', scenario '{scenario}' at {path}:{diffLine}:\n  expected: {e}\n  actual:   {a}";
            }
        }
        return $"View fixture mismatch for app '{appId}', scenario '{scenario}' at {path}.";
    }
}
