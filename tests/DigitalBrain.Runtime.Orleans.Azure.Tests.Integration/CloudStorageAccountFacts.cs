namespace DigitalBrain.Runtime.Orleans.Azure.Tests.Integration;

public sealed class CloudStorageAccountFacts
{
    private const string Azurite = "UseDevelopmentStorage=true";
    private const string Secret = "AccountKey=c2VjcmV0LXZhbHVlLW5vdC10by1iZS1lY2hvZWQ=";

    [Theory]
    [InlineData("https://acct.blob.core.windows.net")]
    [InlineData("https://acct.table.core.windows.net/")]
    [InlineData("https://acct.queue.core.windows.net")]
    public void An_account_uri_derives_all_three_endpoints_with_the_identity(string value)
    {
        var account = CloudStorageAccount.Parse(value);

        Assert.True(account.UsesIdentity);
        Assert.Equal(new Uri("https://acct.blob.core.windows.net/"), account.Blobs().Uri);
        Assert.Equal(new Uri("https://acct.table.core.windows.net/"), account.Tables().Uri);
        Assert.Equal(new Uri("https://acct.queue.core.windows.net/"), account.Queues().Uri);
    }

    [Fact]
    public void A_connection_string_derives_all_three_clients()
    {
        var account = CloudStorageAccount.Parse(Azurite);

        Assert.False(account.UsesIdentity);
        Assert.Equal("devstoreaccount1", account.Blobs().AccountName);
        Assert.Equal("devstoreaccount1", account.Tables().AccountName);
        Assert.Equal("devstoreaccount1", account.Queues().AccountName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("http://acct.blob.core.windows.net")]
    [InlineData("https://example.com")]
    [InlineData("not a connection string")]
    [InlineData("DefaultEndpointsProtocol=https;" + Secret)]
    public void Anything_else_is_an_issue_naming_the_key_and_never_the_value(string? value)
    {
        Assert.Null(CloudStorageAccount.TryParse(value, out var issue));

        Assert.NotNull(issue);
        Assert.Equal(IntoChatOSNames.CloudStorageKey, issue.Key);
        Assert.DoesNotContain("c2VjcmV0", issue.Message, StringComparison.Ordinal);
        var error = Assert.Throws<InvalidOperationException>(() => CloudStorageAccount.Parse(value));
        Assert.Contains(IntoChatOSNames.CloudStorageKey, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("c2VjcmV0", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ToString_never_formats_the_value()
        => Assert.DoesNotContain("devstoreaccount", CloudStorageAccount.Parse(Azurite).ToString(), StringComparison.OrdinalIgnoreCase);
}
