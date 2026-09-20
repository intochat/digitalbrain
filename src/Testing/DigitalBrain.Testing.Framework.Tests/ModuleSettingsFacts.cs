using DigitalBrain.Core;

namespace DigitalBrain.Tests;

public sealed class ModuleSettingsFacts
{
    [Theory]
    [InlineData("Orleans:ClusterId")]
    [InlineData("ConnectionStrings:storage")]
    [InlineData("DigitalBrain:Testing:Overrides")]
    [InlineData("DigitalBrain:Modules:0")]
    [InlineData("DigitalBrain:AI:OpenAI:ApiKey")]
    [InlineData("Provider:PrivateKeyPem")]
    public void HarnessAndCredentialKeysCannotBePublicSettings(string key)
        => Assert.Throws<ArgumentException>(() => ModuleSettingsValidation.ValidatePublicSettings(
            [new(typeof(CompositionFacts.ExampleModule), new Dictionary<string, string?> { [key] = "override" })]));
}
