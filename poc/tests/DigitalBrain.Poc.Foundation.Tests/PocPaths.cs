using System;
using System.IO;

namespace DigitalBrain.Poc.Foundation.Tests;

internal static class PocPaths
{
    public static string Root { get; } = FindRoot();

    public static bool IsInside(string root, string path)
    {
        var physicalRoot = ResolvePhysicalPath(root);
        var physicalPath = ResolvePhysicalPath(path);
        var relativePath = Path.GetRelativePath(physicalRoot, physicalPath);

        return relativePath.Length == 0 ||
            relativePath == "." ||
            (!Path.IsPathRooted(relativePath) &&
             !relativePath.Equals("..", StringComparison.Ordinal) &&
             !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
             !relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal));
    }

    public static string ResolvePhysicalPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var pathRoot = Path.GetPathRoot(fullPath) ?? throw new InvalidOperationException("Path has no root.");
        var currentPath = pathRoot;
        var segments = fullPath[pathRoot.Length..].Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        foreach (var segment in segments)
        {
            var candidatePath = Path.Combine(currentPath, segment);
            var fileSystemInfo = GetExistingFileSystemInfo(candidatePath);
            if (fileSystemInfo is null)
            {
                currentPath = candidatePath;
                continue;
            }

            if ((fileSystemInfo.Attributes & FileAttributes.ReparsePoint) == 0)
            {
                currentPath = candidatePath;
                continue;
            }

            var target = fileSystemInfo.ResolveLinkTarget(returnFinalTarget: true) ??
                throw new InvalidOperationException($"Could not resolve reparse point: {candidatePath}");
            currentPath = Path.GetFullPath(target.FullName);
        }

        return Path.GetFullPath(currentPath);
    }

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DigitalBrain.Poc.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the POC root.");
    }

    private static FileSystemInfo? GetExistingFileSystemInfo(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            return (attributes & FileAttributes.Directory) != 0
                ? new DirectoryInfo(path)
                : new FileInfo(path);
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }
}
