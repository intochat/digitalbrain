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
        var options = new AIOptions { OpenAI = { ApiKey = secret } };
        var composition = Assert.Throws<ArgumentException>(() => new BrainCompositionBuilder()
            .WithModule<AIModule>(ai => ai.WithOptions(options)).Build());
        var overrides = Assert.Throws<ArgumentException>(() => new CompositionOverrides()
            .ConfigureModule<AIModule>(ai => ai.WithOptions(options)).Serialize());
        foreach (var error in new[] { composition, overrides })
        {
            Assert.Contains("private configuration", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(secret, error.ToString());
        }
    }
}