namespace DigitalBrain.Architecture.Tests;

public sealed class ContractRulesFacts
{
    [Fact]
    public void CredentialsAreFoundThroughInheritedNestedAndCollectionOptions()
    {
        var violations = ContractRules.CredentialMembers(typeof(UnsafeOptions)).ToArray();
        Assert.Contains(violations, path => path.EndsWith(".Password", StringComparison.Ordinal));
        Assert.Contains(violations, path => path.EndsWith(".Children[].ApiKey", StringComparison.Ordinal));
        Assert.Contains(violations, path => path.EndsWith(".Nested.RefreshToken", StringComparison.Ordinal));
        Assert.Empty(ContractRules.CredentialMembers(typeof(SafeOptions)));
    }

    [Fact]
    public void TestHooksAreRejectedWithoutBanningLegitimateProductionOperations()
    {
        Assert.Equal(3, ContractRules.TestHooks(typeof(ITestHooks)).Count());
        Assert.Empty(ContractRules.TestHooks(typeof(IProductionOperations)));
    }

    private class PasswordOptions
    {
        public string Password { get; set; } = "";
    }

    private sealed class UnsafeOptions : PasswordOptions
    {
        public KeyOptions[] Children { get; set; } = [];
        public TokenOptions Nested { get; set; } = new();
    }

    private sealed class KeyOptions
    {
        public string ApiKey = "";
    }

    private sealed class TokenOptions
    {
        public string RefreshToken { get; set; } = "";
    }

    private sealed class SafeOptions
    {
        public SafeOptions? Next { get; set; }
        public string AccountId { get; set; } = "";
        public int MaxTokens { get; set; }
    }

    private interface ITestHooks
    {
        void ResetForTesting();
        void TestOnlySeed();
        Task SeedForTestsAsync();
    }

    private interface IProductionOperations
    {
        void Reset();
        void Probe();
        void TestConnection();
    }
}
