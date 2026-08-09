using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml.Linq;

namespace DigitalBrain.Poc.Foundation.Tests;

internal static class ProjectReferenceScanner
{
    private static readonly string[] IgnoredDirectories = ["artifacts", "bin", "candidates", "control-plane-store", "obj"];

    private static readonly string[] StandaloneMsBuildFiles = ["*.props", "*.targets"];

    public static IReadOnlyList<string> ReadAll(string root)
    {
        var paths = new List<string>();
        var normalizedRoot = Path.GetFullPath(root);
        var visitedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var scannedPhysicalFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var project in Directory.EnumerateFiles(normalizedRoot, "*.csproj", SearchOption.AllDirectories))
        {
            ScanProject(normalizedRoot, project, paths, visitedFiles, scannedPhysicalFiles);
        }

        foreach (var pattern in StandaloneMsBuildFiles)
        {
            foreach (var file in Directory.EnumerateFiles(normalizedRoot, pattern, SearchOption.AllDirectories))
            {
                var normalizedFile = Path.GetFullPath(file);
                EnsureFileIsInsidePoc(normalizedRoot, normalizedFile);
                if (IsIgnoredOutputFile(normalizedRoot, normalizedFile))
                {
                    continue;
                }

                var physicalFile = PocPaths.ResolvePhysicalPath(normalizedFile);
                if (IsImplicitDirectoryBuildFile(normalizedFile) && scannedPhysicalFiles.Contains(physicalFile))
                {
                    continue;
                }

                var importingProjectDirectory = IsImplicitDirectoryBuildFile(normalizedFile)
                    ? GetDirectory(physicalFile)
                    : GetDirectory(normalizedFile);
                ScanFile(normalizedRoot, normalizedFile, importingProjectDirectory, paths, visitedFiles, scannedPhysicalFiles);
            }
        }

        return paths;
    }

    private static void ScanProject(
        string root,
        string project,
        List<string> paths,
        HashSet<string> visitedFiles,
        HashSet<string> scannedPhysicalFiles)
    {
        var normalizedProject = Path.GetFullPath(project);
        EnsureFileIsInsidePoc(root, normalizedProject);
        if (IsIgnoredOutputFile(root, normalizedProject))
        {
            return;
        }

        var projectDirectory = GetDirectory(normalizedProject);
        ScanDirectoryBuildFile(root, projectDirectory, "Directory.Build.props", paths, visitedFiles, scannedPhysicalFiles);
        ScanFile(root, normalizedProject, projectDirectory, paths, visitedFiles, scannedPhysicalFiles);
        ScanDirectoryBuildFile(root, projectDirectory, "Directory.Build.targets", paths, visitedFiles, scannedPhysicalFiles);
    }

    private static void ScanDirectoryBuildFile(
        string root,
        string projectDirectory,
        string fileName,
        List<string> paths,
        HashSet<string> visitedFiles,
        HashSet<string> scannedPhysicalFiles)
    {
        var directoryBuildFile = FindNearestDirectoryBuildFile(root, projectDirectory, fileName);
        if (directoryBuildFile is not null)
        {
            ScanFile(root, directoryBuildFile, projectDirectory, paths, visitedFiles, scannedPhysicalFiles);
        }
    }

    private static void ScanFile(
        string root,
        string file,
        string importingProjectDirectory,
        List<string> paths,
        HashSet<string> visitedFiles,
        HashSet<string> scannedPhysicalFiles)
    {
        var normalizedFile = Path.GetFullPath(file);
        EnsureFileIsInsidePoc(root, normalizedFile);

        if (IsIgnoredOutputFile(root, normalizedFile))
        {
            return;
        }

        var normalizedProjectDirectory = Path.GetFullPath(importingProjectDirectory);
        if (!PocPaths.IsInside(root, normalizedProjectDirectory))
        {
            throw new InvalidOperationException($"Importing project directory escapes the POC root: {importingProjectDirectory}");
        }

        var physicalFile = PocPaths.ResolvePhysicalPath(normalizedFile);
        var physicalProjectDirectory = PocPaths.ResolvePhysicalPath(normalizedProjectDirectory);
        if (!visitedFiles.Add(CreateVisitKey(physicalFile, physicalProjectDirectory)))
        {
            return;
        }

        scannedPhysicalFiles.Add(physicalFile);

        var document = XDocument.Load(normalizedFile, LoadOptions.None);
        var importDirectory = GetDirectory(normalizedFile);

        foreach (var element in document.Descendants())
        {
            var name = element.Name.LocalName;
            if (IsElementName(name, "Target") || IsElementName(name, "UsingTask"))
            {
                throw new InvalidOperationException($"Authored MSBuild targets and tasks are not permitted in POC files: {name}");
            }

            if (IsItemElement(element))
            {
                AddAttributePath(root, element, "HintPath", normalizedProjectDirectory, paths);
            }

            if (IsElementName(name, "ProjectReference") || IsElementName(name, "Analyzer"))
            {
                AddAttributePath(root, element, "Include", normalizedProjectDirectory, paths);
                AddAttributePath(root, element, "Update", normalizedProjectDirectory, paths);
            }
            else if (IsElementName(name, "Compile") ||
                     IsElementName(name, "Content") ||
                     IsElementName(name, "None"))
            {
                AddAttributePath(root, element, "Include", normalizedProjectDirectory, paths);
                AddAttributePath(root, element, "Update", normalizedProjectDirectory, paths);
            }
            else if (IsElementName(name, "Import"))
            {
                AddAttributePath(
                    root,
                    element,
                    "Project",
                    importDirectory,
                    paths,
                    importedFile => ScanFile(root, importedFile, normalizedProjectDirectory, paths, visitedFiles, scannedPhysicalFiles));
            }
            else if (IsElementName(name, "HintPath"))
            {
                AddPath(root, element.Value, normalizedProjectDirectory, paths);
            }
        }
    }

    private static bool IsElementName(string actualName, string expectedName) =>
        actualName.Equals(expectedName, StringComparison.OrdinalIgnoreCase);

    private static bool IsItemElement(XElement element)
    {
        var parent = element.Parent;
        return parent is not null &&
            (IsElementName(parent.Name.LocalName, "ItemGroup") ||
             IsElementName(parent.Name.LocalName, "ItemDefinitionGroup"));
    }

    private static void AddAttributePath(
        string root,
        XElement element,
        string attributeName,
        string baseDirectory,
        List<string> paths,
        Action<string>? onPathResolved = null)
    {
        var value = IsElementName(attributeName, "HintPath")
            ? GetHintPathAttributeValue(element)
            : element.Attribute(attributeName)?.Value;
        if (value is not null)
        {
            AddPath(root, value, baseDirectory, paths, onPathResolved);
        }
    }

    private static string? GetHintPathAttributeValue(XElement element)
    {
        foreach (var attribute in element.Attributes())
        {
            if (IsElementName(attribute.Name.LocalName, "HintPath"))
            {
                return attribute.Value;
            }
        }

        return null;
    }

    private static void AddPath(
        string root,
        string value,
        string baseDirectory,
        List<string> paths,
        Action<string>? onPathResolved = null)
    {
        foreach (var pathValue in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            AddSinglePath(root, UnescapeMsBuildPath(pathValue), baseDirectory, paths, onPathResolved);
        }
    }

    private static void AddSinglePath(
        string root,
        string value,
        string baseDirectory,
        List<string> paths,
        Action<string>? onPathResolved)
    {
        if (value.Contains("@(", StringComparison.Ordinal) ||
            value.Contains("%(", StringComparison.Ordinal) ||
            value.Contains("$(", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Hand-authored MSBuild expressions are not permitted in POC paths: {value}");
        }

        if (value.Contains('*') || value.Contains('?'))
        {
            throw new InvalidOperationException($"Hand-authored MSBuild wildcard paths are not permitted in POC: {value}");
        }

        var path = Path.GetFullPath(value, baseDirectory);
        if (!PocPaths.IsInside(root, path))
        {
            throw new InvalidOperationException($"MSBuild path escapes the POC root: {value}");
        }

        if (IsIgnoredOutputFile(root, path))
        {
            throw new InvalidOperationException($"Hand-authored MSBuild paths cannot reference ignored POC output: {value}");
        }

        paths.Add(path);
        onPathResolved?.Invoke(path);
    }

    private static string UnescapeMsBuildPath(string value)
    {
        var percentIndex = value.IndexOf('%');
        if (percentIndex < 0)
        {
            return value;
        }

        StringBuilder? result = null;
        var startIndex = 0;

        while (percentIndex >= 0)
        {
            if (percentIndex <= value.Length - 3 &&
                TryDecodeHexDigit(value[percentIndex + 1], out var highDigit) &&
                TryDecodeHexDigit(value[percentIndex + 2], out var lowDigit))
            {
                result ??= new StringBuilder(value.Length);
                result.Append(value, startIndex, percentIndex - startIndex);
                result.Append((char)((highDigit << 4) + lowDigit));
                startIndex = percentIndex + 3;
            }

            percentIndex = value.IndexOf('%', percentIndex + 1);
        }

        if (result is null)
        {
            return value;
        }

        result.Append(value, startIndex, value.Length - startIndex);
        return result.ToString();
    }

    private static bool TryDecodeHexDigit(char value, out int digit)
    {
        if (value is >= '0' and <= '9')
        {
            digit = value - '0';
            return true;
        }

        if (value is >= 'a' and <= 'f')
        {
            digit = value - 'a' + 10;
            return true;
        }

        if (value is >= 'A' and <= 'F')
        {
            digit = value - 'A' + 10;
            return true;
        }

        digit = 0;
        return false;
    }

    private static string? FindNearestDirectoryBuildFile(string root, string projectDirectory, string fileName)
    {
        for (var directory = new DirectoryInfo(projectDirectory);
             directory is not null && PocPaths.IsInside(root, directory.FullName);
             directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string GetDirectory(string file) =>
        Path.GetDirectoryName(file) ?? throw new InvalidOperationException("MSBuild file has no directory.");

    private static void EnsureFileIsInsidePoc(string root, string file)
    {
        if (!PocPaths.IsInside(root, file))
        {
            throw new InvalidOperationException($"MSBuild file escapes the POC root: {file}");
        }
    }

    private static string CreateVisitKey(string physicalFile, string physicalProjectDirectory) =>
        $"{physicalFile}\0{physicalProjectDirectory}";

    private static bool IsImplicitDirectoryBuildFile(string file)
    {
        var fileName = Path.GetFileName(file);
        return fileName.Equals("Directory.Build.props", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("Directory.Build.targets", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsIgnoredOutputFile(string root, string file)
    {
        return HasIgnoredDirectorySegment(root, file) ||
            HasIgnoredDirectorySegment(PocPaths.ResolvePhysicalPath(root), PocPaths.ResolvePhysicalPath(file));
    }

    private static bool HasIgnoredDirectorySegment(string root, string file)
    {
        var relativePath = Path.GetRelativePath(root, file);
        var segments = relativePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        foreach (var segment in segments)
        {
            foreach (var ignoredDirectory in IgnoredDirectories)
            {
                if (segment.Equals(ignoredDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
