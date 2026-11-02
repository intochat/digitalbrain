using DigitalBrain.Capabilities.Storage.Confinement;

namespace DigitalBrain.Capabilities.Storage.Tests.Unit.Confinement;

public sealed class PathConfinementFacts
{
    private static readonly string[] UnixRoots = ["/data/work"];
    private static readonly string[] WindowsRoots = [@"C:\Work"];

    [Fact]
    public void Relative_path_resolves_inside_the_root()
        => Assert.Equal("/data/work/notes/a.txt", PathConfinement.Resolve(UnixRoots, "notes/a.txt", PathFlavor.Unix));

    [Fact]
    public void Dotdot_escape_is_denied_as_outside()
    {
        var denied = Assert.Throws<ConfinementDenied>(() => PathConfinement.Resolve(UnixRoots, "../../etc/passwd", PathFlavor.Unix));
        Assert.Equal(ConfinementReason.Outside, denied.Reason);
    }

    [Fact]
    public void Absolute_path_outside_the_roots_is_denied()
    {
        var denied = Assert.Throws<ConfinementDenied>(() => PathConfinement.Resolve(UnixRoots, "/data/other/a.txt", PathFlavor.Unix));
        Assert.Equal(ConfinementReason.Outside, denied.Reason);
    }

    [Fact]
    public void Hard_denied_segment_inside_a_root_is_denied_with_the_fixed_message()
    {
        var denied = Assert.Throws<ConfinementDenied>(() => PathConfinement.Resolve(UnixRoots, ".ssh/id_rsa", PathFlavor.Unix));
        Assert.Equal(ConfinementReason.HardDeny, denied.Reason);
        Assert.Equal(ConfinementDenied.FixedMessage, denied.Message);
    }

    [Fact]
    public void State_directory_given_by_the_host_is_denied()
    {
        var denied = Assert.Throws<ConfinementDenied>(() =>
            PathConfinement.Resolve(UnixRoots, "state/x", PathFlavor.Unix, osStateDir: "/data/work/state", secretsDir: null));
        Assert.Equal(ConfinementReason.HardDeny, denied.Reason);
    }

    [Fact]
    public void Empty_and_control_characters_are_denied()
    {
        Assert.Equal(ConfinementReason.Empty, Assert.Throws<ConfinementDenied>(() => PathConfinement.Resolve(UnixRoots, " ", PathFlavor.Unix)).Reason);
        Assert.Equal(ConfinementReason.Control, Assert.Throws<ConfinementDenied>(() => PathConfinement.Resolve(UnixRoots, "a\u0001b", PathFlavor.Unix)).Reason);
    }

    [Theory]
    [InlineData(@"\\?\C:\Work\a", ConfinementReason.Device)]
    [InlineData(@"C:\Work\a.txt:stream", ConfinementReason.Ads)]
    [InlineData(@"C:\Work\PROGRA~1", ConfinementReason.Name)]
    [InlineData(@"C:\Work\..\Windows", ConfinementReason.Outside)]
    public void Windows_flavor_denies_unsafe_names(string input, ConfinementReason reason)
        => Assert.Equal(reason, Assert.Throws<ConfinementDenied>(() => PathConfinement.Resolve(WindowsRoots, input, PathFlavor.Windows)).Reason);

    [Fact]
    public void Windows_flavor_resolves_inside_the_root_case_insensitively()
        => Assert.Equal(@"C:\Work\sub\a.txt", PathConfinement.Resolve(WindowsRoots, "sub/a.txt", PathFlavor.Windows));

    [Fact]
    public void Containing_returns_the_longest_matching_root()
        => Assert.Equal("/data/work/deep", PathConfinement.Containing(["/data/work", "/data/work/deep"], "/data/work/deep/x", PathFlavor.Unix));

    [Fact]
    public void ResolveForCreate_rejects_names_with_separators()
        => Assert.Equal(ConfinementReason.Name, Assert.Throws<ConfinementDenied>(() => PathConfinement.ResolveForCreate(UnixRoots, ".", "a/b", PathFlavor.Unix)).Reason);

    [Fact]
    public void ResolveForCreate_places_the_leaf_under_the_parent()
        => Assert.Equal("/data/work/sub/new.txt", PathConfinement.ResolveForCreate(UnixRoots, "sub", "new.txt", PathFlavor.Unix));

    [Fact]
    public void HardDenyList_names_the_platform_system_directories()
    {
        Assert.Contains("/etc", HardDenyList.For(PathFlavor.Unix, null, null));
        Assert.Contains(@"C:\Windows", HardDenyList.For(PathFlavor.Windows, null, null));
        Assert.Contains(".ssh", HardDenyList.Segments);
    }
}
