using System.Text.Json;
using DigitalBrain.Salesforce;
using DigitalBrain.Sdk;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class SalesforcePolicyTests
{
    [Theory]
    [InlineData("SELECT Id FROM Account WHERE Name = 'Acme' LIMIT 10")]
    [InlineData("SELECT Id, (SELECT Id FROM Contacts) FROM Account WHERE Name = 'WHERE LIMIT -- ;' LIMIT 1 OFFSET 2")]
    public void Bounded_filtered_reads_are_supported(string query) => SalesforceQueryGuard.Validate(query);

    [Theory]
    [InlineData("SELECT Id FROM Account LIMIT 1")]
    [InlineData("SELECT Id FROM Account WHERE Name = 'Acme'")]
    [InlineData("SELECT Id FROM Account WHERE Name = 'Acme' LIMIT 0")]
    [InlineData("SELECT Id FROM Account WHERE Name = 'Acme' LIMIT 1 FOR UPDATE")]
    [InlineData("SELECT Id FROM Account WHERE Name = 'Acme' FOR UPDATE LIMIT 1")]
    [InlineData("SELECT Id, (SELECT Id FROM Contacts WHERE Name='Acme' FOR UPDATE LIMIT 1) FROM Account WHERE Name='Acme' LIMIT 1")]
    [InlineData("SELECT Id FROM Account WHERE Name = 'Acme' LIMIT 1; DELETE FROM Account")]
    [InlineData("SELECT Id FROM Account WHERE Name = 'Acme' /* comment */ LIMIT 1")]
    [InlineData("SELECT Id, (SELECT Id FROM Contacts WHERE Name = 'Acme' LIMIT 1) FROM Account")]
    [InlineData("SELECT Id FROM Account WHERE Name = 'unterminated LIMIT 1")]
    public void Unsafe_queries_are_rejected(string query) => Assert.Throws<ArgumentException>(() => SalesforceQueryGuard.Validate(query));

    [Fact]
    public void Native_json_query_arguments_are_guarded_and_delete_is_absent()
    {
        SalesforceTools.ValidateRead("soqlQuery", new Dictionary<string, object?>
        {
            ["query"] = JsonSerializer.SerializeToElement("SELECT Id FROM Account WHERE Name = 'Acme' LIMIT 10"),
        });
        Assert.Throws<McpOperationException>(() => SalesforceTools.ValidateRead("soqlQuery", new Dictionary<string, object?> { ["query"] = "SELECT Id FROM Account" }));
        Assert.Throws<McpOperationException>(() => SalesforceTools.ValidateRead("deleteRecord", new Dictionary<string, object?>()));
        Assert.DoesNotContain("deleteRecord", SalesforceMcp.AllowedTools);
        Assert.DoesNotContain("createRecord", SalesforceLogins.ReadTools);
        Assert.DoesNotContain("updateRecord", SalesforceLogins.ReadTools);
    }
}
