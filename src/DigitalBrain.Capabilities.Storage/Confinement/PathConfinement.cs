using System.Text;

namespace DigitalBrain.Capabilities.Storage.Confinement;

/// <summary>Which path rules to apply. Model tests pass an explicit flavor so Windows rules run on Linux.</summary>
public enum PathFlavor
{
    Windows,
    Unix,
}

/// <summary>
/// Allow-listed roots, hard-deny, and link resolution shared by local disks and the Windows module.
/// A static class is already sealed; C# rejects <c>sealed static</c>.
/// </summary>
public static class PathConfinement
{
    public const int ModelLengthLimit = 32_000;

    public static PathFlavor Current => OperatingSystem.IsWindows() ? PathFlavor.Windows : PathFlavor.Unix;

    public static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    public static string Resolve(IReadOnlyList<string> roots, string relativeOrRooted)
        => Resolve(roots, relativeOrRooted, Current, osStateDir: null, secretsDir: null);

    public static string Resolve(IReadOnlyList<string> roots, string relativeOrRooted, PathFlavor flavor)
        => Resolve(roots, relativeOrRooted, flavor, osStateDir: null, secretsDir: null);

    public static string Resolve(IReadOnlyList<string> roots, string relativeOrRooted, PathFlavor flavor, string? osStateDir, string? secretsDir)
    {
        ArgumentNullException.ThrowIfNull(roots);
        var full = Prepare(roots, relativeOrRooted, flavor);
        full = Finish(roots, full, flavor, osStateDir, secretsDir, walkLinks: flavor == Current);
        return full;
    }

    public static string Containing(IReadOnlyList<string> roots, string full)
        => Containing(roots, full, Current);

    public static string Containing(IReadOnlyList<string> roots, string full, PathFlavor flavor)
    {
        ArgumentNullException.ThrowIfNull(roots);
        string? best = null;
        foreach (var root in roots)
        {
            var norm = NormRoot(root, flavor);
            if (!IsInside(full, norm, flavor)) { continue; }
            if (best is null || norm.Length > best.Length) { best = norm; }
        }

        return best ?? throw new ConfinementDenied(ConfinementReason.Outside);
    }

    public static string ResolveForCreate(IReadOnlyList<string> roots, string parentRelative, string name, PathFlavor flavor)
        => ResolveForCreate(roots, parentRelative, name, flavor, osStateDir: null, secretsDir: null);

    public static string ResolveForCreate(IReadOnlyList<string> roots, string parentRelative, string name, PathFlavor flavor, string? osStateDir, string? secretsDir)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Contains('/') || name.Contains('\\') || name is "." or "..")
        { throw new ConfinementDenied(ConfinementReason.Name); }
        var parent = Resolve(roots, parentRelative, flavor, osStateDir, secretsDir);
        var leaf = flavor == PathFlavor.Windows ? name.TrimEnd(' ', '.') : name;
        if (leaf.Length == 0) { throw new ConfinementDenied(ConfinementReason.Name); }
        RejectSegment(leaf, flavor);
        var sep = Sep(flavor);
        var full = parent.EndsWith(sep) ? parent + leaf : parent + sep + leaf;
        return Finish(roots, full, flavor, osStateDir, secretsDir, walkLinks: false);
    }

    /// <summary>A write refuses every reparse point. Reads follow a link and re-check the final target.</summary>
    public static void RequireNoLinks(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) { throw new ConfinementDenied(ConfinementReason.Empty); }
        var full = Normalize(path);
        var current = Path.GetPathRoot(full) ?? full;
        foreach (var part in Rest(full, current))
        {
            current = Path.Combine(current, part);
            if (!Exists(current)) { break; }
            var info = Info(current);
            if (info.ResolveLinkTarget(returnFinalTarget: true) is not null)
            { throw new ConfinementDenied(ConfinementReason.LinkEscape); }
        }
    }

    private static string Prepare(IReadOnlyList<string> roots, string relativeOrRooted, PathFlavor flavor)
    {
        if (string.IsNullOrWhiteSpace(relativeOrRooted)) { throw new ConfinementDenied(ConfinementReason.Empty); }
        if (relativeOrRooted.Any(static ch => ch < ' ' || ch == '\u007f')) { throw new ConfinementDenied(ConfinementReason.Control); }
        if (roots.Count == 0) { throw new ConfinementDenied(ConfinementReason.Outside); }
        RejectExtended(relativeOrRooted, flavor);

        if (flavor == Current)
        {
            var combined = Path.IsPathRooted(relativeOrRooted) ? relativeOrRooted : Path.Combine(roots[0], relativeOrRooted);
            try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(combined)); }
            catch (PathTooLongException) { throw new ConfinementDenied(ConfinementReason.TooLong); }
            catch (ArgumentException) { throw new ConfinementDenied(ConfinementReason.Name); }
        }

        return Lexical(NormRoot(roots[0], flavor), relativeOrRooted, flavor);
    }

    private static string Finish(IReadOnlyList<string> roots, string full, PathFlavor flavor, string? osStateDir, string? secretsDir, bool walkLinks)
    {
        if (full.Length > ModelLengthLimit) { throw new ConfinementDenied(ConfinementReason.TooLong); }
        var root = Containing(roots, full, flavor);
        Deny(full, flavor, osStateDir, secretsDir);
        foreach (var part in Relative(full, root, flavor)) { RejectSegment(part, flavor); }
        if (!walkLinks) { return full; }

        var linked = Walk(full);
        if (string.Equals(linked, full, Comparison(flavor))) { return full; }
        if (linked.Length > ModelLengthLimit) { throw new ConfinementDenied(ConfinementReason.TooLong); }
        root = Containing(roots, linked, flavor);
        Deny(linked, flavor, osStateDir, secretsDir);
        foreach (var part in Relative(linked, root, flavor)) { RejectSegment(part, flavor); }
        return linked;
    }

    private static string Walk(string full)
    {
        var root = Path.GetPathRoot(full) ?? "";
        var parts = Rest(full, root).ToList();
        var current = root;
        for (var i = 0; i < parts.Count; i++)
        {
            var next = Path.Combine(current, parts[i]);
            if (!Exists(next))
            {
                for (var j = i; j < parts.Count; j++) { current = Path.Combine(current, parts[j]); }
                return Path.TrimEndingDirectorySeparator(current);
            }

            var target = Info(next).ResolveLinkTarget(returnFinalTarget: true);
            current = target is null ? next : Path.TrimEndingDirectorySeparator(target.FullName);
        }

        return Path.TrimEndingDirectorySeparator(current);
    }

    private static void Deny(string full, PathFlavor flavor, string? osStateDir, string? secretsDir)
    {
        var compare = DenyComparison(flavor);
        foreach (var prefix in HardDenyList.For(flavor, osStateDir, secretsDir))
        {
            var norm = flavor == Current ? SafeNorm(prefix) : prefix.Replace(Alt(flavor), Sep(flavor));
            if (IsInside(full, TrimSep(norm, flavor), flavor) || IsInside(NormForDeny(full), NormForDeny(norm), flavor))
            { throw new ConfinementDenied(ConfinementReason.HardDeny); }
        }

        foreach (var part in SegmentsOf(full, flavor))
        {
            var name = flavor == PathFlavor.Windows ? part.TrimEnd(' ', '.') : part;
            foreach (var denied in HardDenyList.Segments)
            {
                if (string.Equals(name, denied, compare)) { throw new ConfinementDenied(ConfinementReason.HardDeny); }
            }
        }
    }

    private static string NormForDeny(string path)
        => path.Normalize(NormalizationForm.FormC);

    private static StringComparison DenyComparison(PathFlavor flavor)
        => flavor == PathFlavor.Windows || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static void RejectExtended(string path, PathFlavor flavor)
    {
        if (flavor != PathFlavor.Windows) { return; }
        var text = path.Replace('/', '\\');
        if (text.StartsWith(@"\\?\", StringComparison.Ordinal) || text.StartsWith(@"\\.\", StringComparison.Ordinal))
        { throw new ConfinementDenied(ConfinementReason.Device); }
    }

    private static string[] Relative(string full, string root, PathFlavor flavor)
    {
        var compare = Comparison(flavor);
        var sep = Sep(flavor);
        var path = TrimSep(full.Replace(Alt(flavor), sep), flavor);
        var basePath = TrimSep(root.Replace(Alt(flavor), sep), flavor);
        if (string.Equals(path, basePath, compare)) { return []; }
        var prefix = basePath.EndsWith(sep) ? basePath : basePath + sep;
        if (!path.StartsWith(prefix, compare)) { return []; }
        return path[prefix.Length..].Split(sep, StringSplitOptions.RemoveEmptyEntries);
    }

    private static void RejectSegment(string segment, PathFlavor flavor)
    {
        if (flavor != PathFlavor.Windows) { return; }
        if (segment.Contains(':')) { throw new ConfinementDenied(ConfinementReason.Ads); }
        var trimmed = segment.TrimEnd(' ', '.');
        if (trimmed.Length == 0) { throw new ConfinementDenied(ConfinementReason.Name); }
        if (IsDevice(trimmed)) { throw new ConfinementDenied(ConfinementReason.Device); }
        if (IsShort(trimmed)) { throw new ConfinementDenied(ConfinementReason.Name); }
    }

    private static bool IsDevice(string segment)
    {
        var name = segment;
        var dot = segment.IndexOf('.', StringComparison.Ordinal);
        if (dot >= 0) { name = segment[..dot]; }
        return name.Equals("CON", StringComparison.OrdinalIgnoreCase)
            || name.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || name.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            || name.Equals("NUL", StringComparison.OrdinalIgnoreCase)
            || (name.Length == 4 && name.StartsWith("COM", StringComparison.OrdinalIgnoreCase) && name[3] is >= '1' and <= '9')
            || (name.Length == 4 && name.StartsWith("LPT", StringComparison.OrdinalIgnoreCase) && name[3] is >= '1' and <= '9');
    }

    private static bool IsShort(string segment)
    {
        var name = segment;
        var dot = segment.IndexOf('.', StringComparison.Ordinal);
        if (dot >= 0) { name = segment[..dot]; }
        var tilde = name.LastIndexOf('~');
        if (tilde <= 0 || tilde > 6) { return false; }
        if (name.Length != tilde + 2) { return false; }
        return name[tilde + 1] is >= '0' and <= '9';
    }

    private static string Lexical(string root, string input, PathFlavor flavor)
    {
        var sep = Sep(flavor);
        var text = input.Replace(Alt(flavor), sep);
        var rooted = flavor == PathFlavor.Windows ? IsWindowsRooted(text) : text.StartsWith('/');
        var combined = rooted ? text : Combine(root, text, sep);
        if (flavor == PathFlavor.Windows && combined.StartsWith(@"\\", StringComparison.Ordinal))
        { return LexicalUnc(combined); }
        if (flavor == PathFlavor.Windows && combined.Length >= 2 && combined[1] == ':')
        { return LexicalDrive(combined); }
        return LexicalUnix(combined);
    }

    private static string LexicalDrive(string combined)
    {
        var drive = char.ToUpperInvariant(combined[0]) + ":\\";
        var rest = combined.Length > 3 ? combined[3..] : "";
        var parts = new List<string>();
        foreach (var part in rest.Split('\\', StringSplitOptions.RemoveEmptyEntries)) { Push(parts, part); }

        return parts.Count == 0 ? drive : drive + string.Join('\\', parts);
    }

    private static string LexicalUnc(string combined)
    {
        var bits = combined.TrimStart('\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (bits.Length < 2) { throw new ConfinementDenied(ConfinementReason.Unc); }
        var parts = new List<string>();
        for (var i = 2; i < bits.Length; i++) { Push(parts, bits[i]); }

        var prefix = @"\\" + bits[0] + @"\" + bits[1];
        return parts.Count == 0 ? prefix : prefix + @"\" + string.Join('\\', parts);
    }

    private static string LexicalUnix(string combined)
    {
        var parts = new List<string>();
        foreach (var part in combined.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".") { continue; }
            if (part == "..")
            {
                if (parts.Count == 0) { throw new ConfinementDenied(ConfinementReason.Outside); }
                parts.RemoveAt(parts.Count - 1);
                continue;
            }
            parts.Add(part);
        }

        return "/" + string.Join('/', parts);
    }

    /// <summary>
    /// Parent and current segments are decided before trailing dots are stripped.
    /// <c>"..".TrimEnd(' ', '.')</c> is empty, so stripping first drops the parent instead of walking it.
    /// </summary>
    private static void Push(List<string> parts, string part)
    {
        var spaced = part.TrimEnd(' ');
        if (spaced == ".") { return; }
        if (spaced == "..")
        {
            if (parts.Count == 0) { throw new ConfinementDenied(ConfinementReason.Outside); }
            parts.RemoveAt(parts.Count - 1);
            return;
        }

        var name = spaced.TrimEnd('.');
        if (name.Length == 0 || name == ".") { return; }
        parts.Add(name);
    }

    private static bool IsWindowsRooted(string text)
        => text.StartsWith(@"\\", StringComparison.Ordinal) || (text.Length >= 2 && text[1] == ':');

    private static string NormRoot(string root, PathFlavor flavor)
    {
        if (flavor == Current) { return Normalize(root); }
        var sep = Sep(flavor);
        var text = root.Replace(Alt(flavor), sep);
        return TrimSep(text, flavor);
    }

    private static string SafeNorm(string path)
    {
        try { return Normalize(path); }
        catch (ArgumentException) { return path; }
        catch (NotSupportedException) { return path; }
    }

    public static bool IsInside(string full, string root, PathFlavor flavor)
    {
        var compare = Comparison(flavor);
        full = TrimSep(full, flavor);
        root = TrimSep(root, flavor);
        if (string.Equals(full, root, compare)) { return true; }
        var sep = Sep(flavor);
        var prefix = root.EndsWith(sep) ? root : root + sep;
        return full.StartsWith(prefix, compare);
    }

    private static StringComparison Comparison(PathFlavor flavor)
        => flavor == PathFlavor.Windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static char Sep(PathFlavor flavor) => flavor == PathFlavor.Windows ? '\\' : '/';

    private static char Alt(PathFlavor flavor) => flavor == PathFlavor.Windows ? '/' : '\\';

    private static string TrimSep(string path, PathFlavor flavor)
    {
        var sep = Sep(flavor);
        if (flavor == PathFlavor.Windows && path.Length == 3 && path[1] == ':' && path[2] == '\\') { return path; }
        if (flavor == PathFlavor.Unix && path == "/") { return path; }
        return path.TrimEnd(sep);
    }

    private static string Combine(string root, string relative, char sep)
    {
        if (root.EndsWith(sep)) { return root + relative.TrimStart(sep); }
        return root + sep + relative.TrimStart(sep);
    }

    private static IEnumerable<string> SegmentsOf(string full, PathFlavor flavor)
    {
        var sep = Sep(flavor);
        var text = full.Replace(Alt(flavor), sep);
        if (flavor == PathFlavor.Windows && text.Length >= 2 && text[1] == ':')
        { text = text.Length > 2 ? text[2..] : ""; }
        else if (flavor == PathFlavor.Windows && text.StartsWith(@"\\", StringComparison.Ordinal))
        {
            var bits = text.TrimStart('\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
            return bits.Skip(2);
        }

        return text.Split(sep, StringSplitOptions.RemoveEmptyEntries);
    }

    private static string[] Rest(string full, string root)
        => full.Length <= root.Length
            ? []
            : full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

    private static bool Exists(string path)
        => File.Exists(path) || Directory.Exists(path);

    private static FileSystemInfo Info(string path)
        => Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
}
