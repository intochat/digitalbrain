using DigitalBrain.Kernel.Runtime;

namespace DigitalBrain.Tests.Runtime;

public sealed class InoMutationGrantTests
{
    [Theory]
    [InlineData(GmailTools.Send, "gmail.send")]
    [InlineData(SalesforceTools.UpdateRecord, "salesforce.write")]
    public void Approval_requires_the_provider_write_grant(string toolId, string requiredGrant)
    {
        Assert.Throws<UnauthorizedAccessException>(() => InoMutationGrants.Demand(
            toolId,
            new HashSet<string>(["ui.action"], StringComparer.Ordinal)));
        InoMutationGrants.Demand(
            toolId,
            new HashSet<string>(["ui.action", requiredGrant], StringComparer.Ordinal));
    }

    [Fact]
    public void Unknown_typed_tools_keep_the_existing_approval_policy() =>
        InoMutationGrants.Demand(
            "test.effect",
            new HashSet<string>(["ui.action"], StringComparer.Ordinal));
}
