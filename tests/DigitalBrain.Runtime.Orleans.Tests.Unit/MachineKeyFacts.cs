using Microsoft.Extensions.Configuration;
using Orleans.Runtime;
using Orleans.Storage;

namespace DigitalBrain.Runtime.Orleans.Tests.Unit;

public sealed class MachineKeyFacts : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "db-mk-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { if (Directory.Exists(_home)) { Directory.Delete(_home, recursive: true); } }

    private StateHome Home() => StateHome.Resolve(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { [IntoChatOSNames.HomeKey] = _home }).Build());

    private string? Key(IConfiguration? existing = null)
        => new ConfigurationBuilder().Add(new MachineKeyConfigurationSource(Home(), existing)).Build()[IntoChatOSNames.MasterKeyKey];

    [Fact]
    public void First_start_creates_the_key_file_and_supplies_a_32_byte_base64_key()
    {
        var key = Key();
        Assert.True(File.Exists(Path.Combine(_home, "secrets", MachineKeyConfigurationSource.FileName)));
        Assert.Equal(32, Convert.FromBase64String(key!).Length);
    }

    [Fact]
    public void Second_start_returns_the_same_key() => Assert.Equal(Key(), Key());

    [Fact]
    public void An_explicit_master_key_wins_and_no_file_is_created()
    {
        var explicitKey = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [IntoChatOSNames.MasterKeyKey] = "explicit" }).Build();
        Assert.Null(Key(explicitKey));
        Assert.False(File.Exists(Path.Combine(_home, "secrets", MachineKeyConfigurationSource.FileName)));
    }

    [Fact]
    public void A_missing_key_file_while_state_holds_data_is_a_named_error_and_no_key_is_minted()
    {
        Directory.CreateDirectory(Path.Combine(_home, "state", "files"));
        File.WriteAllText(Path.Combine(_home, "state", "files", "x.json"), "{}");

        var error = Assert.Throws<InvalidOperationException>(() => Key());

        Assert.Contains(IntoChatOSNames.MasterKeyKey, error.Message, StringComparison.Ordinal);
        Assert.Contains(MachineKeyConfigurationSource.FileName, error.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(_home, "secrets", MachineKeyConfigurationSource.FileName)));
    }

    [Fact]
    public void On_unix_the_key_file_is_0600()
    {
        if (OperatingSystem.IsWindows()) { return; } // Unix-only fact; Windows uses DPAPI and the protected ACL.
        Key();
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite,
            File.GetUnixFileMode(Path.Combine(_home, "secrets", MachineKeyConfigurationSource.FileName)));
    }

    [Fact]
    public void On_windows_the_file_content_is_not_the_raw_key()
    {
        if (!OperatingSystem.IsWindows()) { return; } // Windows-only fact.
        var key = Convert.FromBase64String(Key()!);
        var stored = File.ReadAllBytes(Path.Combine(_home, "secrets", MachineKeyConfigurationSource.FileName));
        Assert.NotEqual(key, stored);
        Assert.False(stored.AsSpan().IndexOf(key) >= 0);
    }

    [Fact]
    public async Task Unregistered_cluster_storage_names_AddBrainStorage_on_every_member()
    {
        var storage = new UnregisteredClusterStorage();
        var grain = GrainId.Create("probe", "k");
        var state = new GrainState<int>();
        foreach (var call in new Func<Task>[]
        {
            () => storage.ReadStateAsync("s", grain, state),
            () => storage.WriteStateAsync("s", grain, state),
            () => storage.ClearStateAsync("s", grain, state),
        })
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(call);
            Assert.Contains("AddBrainStorage", error.Message, StringComparison.Ordinal);
        }
    }
}
