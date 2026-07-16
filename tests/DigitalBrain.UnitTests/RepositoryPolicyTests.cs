using System.Diagnostics;
using System.Text.RegularExpressions;
using Xunit;

namespace DigitalBrain.UnitTests;

public sealed class RepositoryPolicyTests
{
    private static readonly string[] FlutterGeneratedArtifacts =
    [
        "app/linux/flutter/generated_plugin_registrant.cc",
        "app/linux/flutter/generated_plugin_registrant.h",
        "app/linux/flutter/generated_plugins.cmake",
        "app/macos/Flutter/GeneratedPluginRegistrant.swift",
        "app/windows/flutter/generated_plugin_registrant.cc",
        "app/windows/flutter/generated_plugin_registrant.h",
        "app/windows/flutter/generated_plugins.cmake"
    ];

    private static readonly HashSet<string> CStyleExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".dart", ".cpp", ".cc", ".h", ".swift", ".kt", ".kts", ".proto", ".frag", ".rc", ".pbxproj"
    };

    private static readonly HashSet<string> XmlExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".xml", ".csproj", ".props", ".targets", ".slnx", ".plist", ".storyboard", ".xib", ".entitlements",
        ".xcscheme", ".xcworkspacedata", ".manifest"
    };

    private static readonly HashSet<string> HashExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".yaml", ".yml", ".toml", ".properties", ".xcconfig", ".cmake"
    };

    [Fact]
    public void Tracked_source_and_configuration_files_are_comment_free()
    {
        var root = FindRepositoryRoot();
        var violations = TrackedFiles(root)
            .Select(path => FindViolation(root, path))
            .Where(violation => violation is not null)
            .ToArray();

        Assert.True(violations.Length == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Flutter_generated_artifacts_are_ignored_and_untracked()
    {
        var root = FindRepositoryRoot();
        var trackedFiles = TrackedFiles(root);

        foreach (var path in FlutterGeneratedArtifacts)
        {
            Assert.DoesNotContain(path, trackedFiles);
            Assert.Equal(0, GitExitCode(root, "check-ignore", "--no-index", "-q", "--", path));
        }
    }

    [Fact]
    public void Repository_has_the_approved_project_shape()
    {
        var root = FindRepositoryRoot();
        var projects = TrackedFiles(root)
            .Where(path => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.Equal(24, projects.Length);
        Assert.Contains("app/Flutter.proj", TrackedFiles(root));
        Assert.DoesNotContain(projects, path =>
            path.Contains("DigitalBrain.Core", StringComparison.Ordinal) ||
            path.Contains("DigitalBrain.Kernel.Abstractions", StringComparison.Ordinal) ||
            path.Contains("DigitalBrain.TestKit", StringComparison.Ordinal));
    }

    [Fact]
    public void Production_code_has_no_deleted_namespaces_or_provider_shaped_kernel_types()
    {
        var root = FindRepositoryRoot();
        var violations = TrackedFiles(root)
            .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .Where(IsProductionCode)
            .Select(path => (Path: path, Text: File.ReadAllText(Path.Combine(root, path))))
            .Where(file =>
                file.Text.Contains("DigitalBrain.Core", StringComparison.Ordinal) ||
                file.Text.Contains("DigitalBrain.Kernel.Abstractions", StringComparison.Ordinal) ||
                IsKernelPath(file.Path) && ContainsProviderReference(file.Text))
            .Select(file => file.Path)
            .ToArray();

        Assert.True(violations.Length == 0, string.Join(Environment.NewLine, violations));
    }

    private static bool IsProductionCode(string path) =>
        path.StartsWith("src/", StringComparison.Ordinal) ||
        path.StartsWith("hosts/", StringComparison.Ordinal) ||
        path.StartsWith("integrations/", StringComparison.Ordinal) ||
        path.StartsWith("features/", StringComparison.Ordinal) &&
        !path.Contains(".Tests/", StringComparison.Ordinal);

    private static bool IsKernelPath(string path) =>
        path.StartsWith("src/DigitalBrain.Kernel/", StringComparison.Ordinal) ||
        path.StartsWith("src/DigitalBrain.Kernel.Contracts/", StringComparison.Ordinal);

    private static bool ContainsProviderReference(string text) =>
        Regex.IsMatch(text, "gmail|google|salesforce", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static string? FindViolation(string root, string relativePath)
    {
        var path = Path.Combine(root, relativePath);
        if (!File.Exists(path))
        {
            return null;
        }

        var extension = Path.GetExtension(path);
        var text = File.ReadAllText(path);
        var index = CStyleExtensions.Contains(extension)
            ? FindCStyleComment(text, extension.Equals(".dart", StringComparison.OrdinalIgnoreCase))
            : XmlExtensions.Contains(extension)
                ? text.IndexOf("<!--", StringComparison.Ordinal)
                : HashExtensions.Contains(extension)
                    ? FindHashComment(text)
                    : -1;

        return index < 0 ? null : $"{relativePath}:{LineNumber(text, index)} contains a comment";
    }

    private static int FindCStyleComment(string text, bool dart)
    {
        var index = 0;
        while (index < text.Length)
        {
            if (StartsWith(text, index, "//") || StartsWith(text, index, "/*"))
            {
                return index;
            }

            if (TrySkipString(text, index, dart, out var end))
            {
                index = end;
                continue;
            }

            index++;
        }

        return -1;
    }

    private static int FindHashComment(string text)
    {
        var quote = '\0';
        var escaped = false;
        for (var index = 0; index < text.Length; index++)
        {
            var current = text[index];
            if (current is '\r' or '\n')
            {
                quote = '\0';
                escaped = false;
                continue;
            }

            if (quote != '\0')
            {
                if (quote == '"' && current == '\\' && !escaped)
                {
                    escaped = true;
                    continue;
                }

                if (current == quote && !escaped)
                {
                    quote = '\0';
                }

                escaped = false;
                continue;
            }

            if (current is '\'' or '"')
            {
                quote = current;
            }
            else if (current == '#')
            {
                return index;
            }
        }

        return -1;
    }

    private static bool TrySkipString(string text, int start, bool dart, out int end)
    {
        end = start;
        var index = start;
        var verbatim = false;
        var raw = false;

        if (dart && index + 1 < text.Length && text[index] is 'r' or 'R' && text[index + 1] is '\'' or '"')
        {
            raw = true;
            index++;
        }
        else if (!dart)
        {
            var prefixStart = index;
            while (index < text.Length && text[index] is '$' or '@')
            {
                verbatim |= text[index] == '@';
                index++;
            }

            if (index == prefixStart && text[index] is not '\'' and not '"')
            {
                return false;
            }
        }

        if (index >= text.Length || text[index] is not '\'' and not '"')
        {
            return false;
        }

        var quote = text[index];
        var quoteCount = 1;
        while (index + quoteCount < text.Length && text[index + quoteCount] == quote)
        {
            quoteCount++;
        }

        var multiple = dart && quoteCount >= 3 || !dart && quote == '"' && quoteCount >= 3;
        var delimiterLength = multiple ? quoteCount : 1;
        index += delimiterLength;

        while (index < text.Length)
        {
            if (multiple && HasQuotes(text, index, quote, delimiterLength))
            {
                end = index + delimiterLength;
                return true;
            }

            if (!multiple && text[index] == quote)
            {
                if (verbatim && quote == '"' && index + 1 < text.Length && text[index + 1] == '"')
                {
                    index += 2;
                    continue;
                }

                end = index + 1;
                return true;
            }

            if (!raw && !verbatim && !multiple && text[index] == '\\' && index + 1 < text.Length)
            {
                index += 2;
                continue;
            }

            index++;
        }

        end = text.Length;
        return true;
    }

    private static bool HasQuotes(string text, int index, char quote, int count)
    {
        if (index + count > text.Length)
        {
            return false;
        }

        for (var offset = 0; offset < count; offset++)
        {
            if (text[index + offset] != quote)
            {
                return false;
            }
        }

        return true;
    }

    private static bool StartsWith(string text, int index, string value)
    {
        return index + value.Length <= text.Length && text.AsSpan(index, value.Length).SequenceEqual(value);
    }

    private static int LineNumber(string text, int index)
    {
        return text.AsSpan(0, index).Count('\n') + 1;
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Brain.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Unable to locate Brain.slnx.");
    }

    private static IReadOnlyList<string> TrackedFiles(string root)
    {
        var startInfo = new ProcessStartInfo("git", "ls-files -z")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Unable to start git.");
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
        return output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    private static int GitExitCode(string root, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = root,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Unable to start git.");
        process.WaitForExit();
        return process.ExitCode;
    }
}
