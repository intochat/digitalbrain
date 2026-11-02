namespace DigitalBrain.Runtime.Orleans.Tests.Unit;

public sealed class TopologyValidatorFacts
{
    private const string Sentinel = "s3cr3t-SENTINEL-value";

    [Theory]
    [InlineData("local", "IntoChatOS:Cloud:Storage")]
    [InlineData("local+cloud", "IntoChatOS:Cloud:Storage")]
    [InlineData("local", "IntoChatOS:Cloud:KeyVault")]
    [InlineData("local+cloud", "IntoChatOS:Cloud:KeyVault")]
    public void Cloud_keys_under_non_cloud_topology_require_cloud(string topology, string key)
    {
        var pairs = new List<(string, string?)> { (RuntimeOptions.IntoChatCompatible.TopologyKey, topology), (key, Sentinel) };
        if (topology == "local+cloud") { pairs.Add((RuntimeOptions.IntoChatCompatible.CloudServerAddressKey, Sentinel)); }

        var issues = TopologyValidator.Validate(Config.Of([.. pairs]), []);

        var issue = Assert.Single(issues);
        Assert.Equal(key, issue.Key);
        Assert.Contains("requires Topology=cloud", issue.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("local")]
    [InlineData("cloud")]
    public void Server_address_requires_local_cloud(string topology)
    {
        var pairs = new List<(string, string?)> { (RuntimeOptions.IntoChatCompatible.TopologyKey, topology), (RuntimeOptions.IntoChatCompatible.CloudServerAddressKey, Sentinel) };
        if (topology == "cloud")
        {
            pairs.Add((RuntimeOptions.IntoChatCompatible.CloudStorageKey, Sentinel));
            pairs.Add((RuntimeOptions.IntoChatCompatible.CloudKeyVaultKey, Sentinel));
        }

        var issue = Assert.Single(TopologyValidator.Validate(Config.Of([.. pairs]), []));
        Assert.Equal(RuntimeOptions.IntoChatCompatible.CloudServerAddressKey, issue.Key);
        Assert.Contains("requires Topology=local+cloud", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Cloud_missing_storage_is_an_issue_and_a_missing_vault_is_a_warning()
    {
        // 04-CONTEXT R4-08: the vault is optional; without it secrets come from the machine key.
        var settings = TopologySettings.Parse(Config.Of((RuntimeOptions.IntoChatCompatible.TopologyKey, "cloud")), []);

        Assert.Equal([RuntimeOptions.IntoChatCompatible.CloudStorageKey], settings.Issues.Select(i => i.Key));
        Assert.Contains(settings.Warnings, w => w.Contains(RuntimeOptions.IntoChatCompatible.CloudKeyVaultKey, StringComparison.Ordinal));
    }

    [Fact]
    public void Local_cloud_missing_server_address_is_an_issue()
    {
        var issue = Assert.Single(TopologyValidator.Validate(Config.Of((RuntimeOptions.IntoChatCompatible.TopologyKey, "local+cloud")), []));
        Assert.Equal(RuntimeOptions.IntoChatCompatible.CloudServerAddressKey, issue.Key);
    }

    [Fact]
    public void Retired_key_names_its_replacement()
    {
        RetiredSetting[] retired = [new("IntoChatOS:Preset", "IntoChatOS:Topology")];

        var issues = TopologyValidator.Validate(Config.Of((RuntimeOptions.IntoChatCompatible.TopologyKey, "local"), ("IntoChatOS:Preset", Sentinel)), retired);

        var issue = Assert.Single(issues);
        Assert.Equal("IntoChatOS:Preset", issue.Key);
        Assert.Contains("IntoChatOS:Topology", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Retired_key_without_topology_also_asks_to_set_topology()
    {
        RetiredSetting[] retired = [new("IntoChatOS:Preset", "IntoChatOS:Topology")];

        var issues = TopologyValidator.Validate(Config.Of(("IntoChatOS:Preset", Sentinel)), retired);

        Assert.Equal(["IntoChatOS:Preset", RuntimeOptions.IntoChatCompatible.TopologyKey], issues.Select(i => i.Key));
    }

    [Fact]
    public void Messages_never_contain_configured_values()
    {
        RetiredSetting[] retired = [new("IntoChatOS:Profile", "IntoChatOS:Topology")];
        var configuration = Config.Of(
            (RuntimeOptions.IntoChatCompatible.TopologyKey, "local"),
            (RuntimeOptions.IntoChatCompatible.CloudStorageKey, Sentinel),
            (RuntimeOptions.IntoChatCompatible.CloudKeyVaultKey, Sentinel),
            (RuntimeOptions.IntoChatCompatible.CloudServerAddressKey, Sentinel),
            ("IntoChatOS:Profile", Sentinel));

        var issues = TopologyValidator.Validate(configuration, retired);

        Assert.Equal(4, issues.Count);
        var thrown = Assert.Throws<InvalidOperationException>(() => TopologyValidator.ThrowIfInvalid(issues));
        Assert.DoesNotContain(Sentinel, thrown.Message, StringComparison.Ordinal);
        Assert.All(issues, issue => Assert.DoesNotContain(Sentinel, issue.ToString(), StringComparison.Ordinal));
    }

    [Fact]
    public void ThrowIfInvalid_throws_once_listing_every_issue_and_passes_when_empty()
    {
        TopologyValidator.ThrowIfInvalid([]);
        var thrown = Assert.Throws<InvalidOperationException>(() =>
            TopologyValidator.ThrowIfInvalid([new TopologyIssue("a", "one."), new TopologyIssue("b", "two.")]));
        Assert.Contains("a: one.", thrown.Message, StringComparison.Ordinal);
        Assert.Contains("b: two.", thrown.Message, StringComparison.Ordinal);
    }
}
