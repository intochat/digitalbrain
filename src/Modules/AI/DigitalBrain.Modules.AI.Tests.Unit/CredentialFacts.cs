using DigitalBrain.AI;
using DigitalBrain.Core;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class CredentialFacts
{
    [Fact]
    public void TypedApiKeysAreRejectedAndNeverEchoed()
    {
        const string secret = "must-not-appear-in-errors";
        var composition = Assert.Throws<ArgumentException>(() => new BrainCompositionBuilder()
            .WithModule<AIModule, AIOptions>(ai => ai.OpenAI.ApiKey = secret).Build());
        var overrides = Assert.Throws<ArgumentException>(() => new BrainCompositionBuilder().WithModule<AIModule, AIOptions>()
            .ApplyOverrides(new CompositionOverrides().ConfigureModule<AIModule, AIOptions>(ai => ai.OpenAI.ApiKey = secret).Serialize()));
        foreach (var error in new[] { composition, overrides })
        {
            Assert.Contains("private configuration", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(secret, error.ToString());
        }
    }
}