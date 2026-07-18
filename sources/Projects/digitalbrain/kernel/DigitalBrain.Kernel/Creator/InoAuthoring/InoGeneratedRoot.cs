namespace DigitalBrain.Kernel.Creator.InoAuthoring;

// E-SDK #57 sub-issue B. Production default — locates the
// `DigitalBrain.Domains.Dynamic/Generated/` directory by walking up from
// AppContext.BaseDirectory until it finds a marker file. The fallback
// `%LocalAppData%\DigitalBrain\InoGenerated` keeps a deployed silo (no repo
// at runtime) functional without requiring a source-tree path. The
// directory is created on first use.
public sealed class InoGeneratedRoot : IInoGeneratedRoot
{
    public InoGeneratedRoot()
    {
        AbsolutePath = ResolveRoot();
        Directory.CreateDirectory(AbsolutePath);
    }

    public string AbsolutePath { get; }

    static string ResolveRoot()
    {
        // Walk up from the running silo's binary directory looking for the
        // repository's Dynamic-domain Generated subtree. Bounded to 8
        // levels so a deployed binary outside the repo falls through
        // quickly to the LocalAppData fallback.
        var probe = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && probe is not null; i++)
        {
            var candidate = Path.Combine(
                probe, "kernel",
                "DigitalBrain.Domains.Dynamic", "Generated");
            // `.git` is a directory in a normal clone and a FILE in a git
            // worktree / submodule — accept either so the source-tree
            // resolution still wins over the LocalAppData fallback when
            // the silo runs out of a worktree checkout.
            var gitPath = Path.Combine(probe, ".git");
            if ((Directory.Exists(gitPath) || File.Exists(gitPath))
                && File.Exists(Path.Combine(probe, "CLAUDE.md")))
                return candidate;
            probe = Path.GetDirectoryName(probe);
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DigitalBrain", "InoGenerated");
    }
}
