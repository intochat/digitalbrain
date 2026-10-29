namespace DigitalBrain.Runtime.Orleans.Tests.Unit;

public sealed class StateHomeFacts
{
    private static string UserFolder => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    [Fact]
    public void Default_is_dot_intochat_under_the_user_folder()
    {
        Assert.Equal(Path.Combine(UserFolder, ".intochat"), StateHome.Resolve(Config.Of()).Path);
        Assert.Equal(StateHome.Default(), StateHome.Resolve(null));
    }

    [Fact]
    public void Leading_tilde_expands()
    {
        var home = StateHome.Resolve(Config.Of((IntoChatOSNames.HomeKey, "~/custom-home")));
        Assert.Equal(Path.Combine(UserFolder, "custom-home"), home.Path);
    }

    [Fact]
    public void Relative_path_is_rejected_naming_the_key()
    {
        var thrown = Assert.Throws<InvalidOperationException>(() => StateHome.Resolve(Config.Of((IntoChatOSNames.HomeKey, "rel/home"))));
        Assert.Contains(IntoChatOSNames.HomeKey, thrown.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("rel/home", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Existing_file_is_rejected_naming_the_key()
    {
        var file = Path.GetTempFileName();
        try
        {
            var thrown = Assert.Throws<InvalidOperationException>(() => StateHome.Resolve(Config.Of((IntoChatOSNames.HomeKey, file))));
            Assert.Contains(IntoChatOSNames.HomeKey, thrown.Message, StringComparison.Ordinal);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void EnsureCreated_creates_the_four_folders_and_secrets_is_owner_only()
    {
        var root = Path.Combine(Path.GetTempPath(), "db-home-" + Guid.NewGuid().ToString("N"));
        try
        {
            var home = StateHome.Resolve(Config.Of((IntoChatOSNames.HomeKey, root))).EnsureCreated();

            Assert.Equal(Path.Combine(root, "state"), home.State);
            Assert.All(new[] { home.State, home.Secrets, home.Apps, home.Logs }, folder => Assert.True(Directory.Exists(folder), folder));
            if (!OperatingSystem.IsWindows())
            {
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(home.Secrets));
            }
        }
        finally { if (Directory.Exists(root)) { Directory.Delete(root, recursive: true); } }
    }
}
