using DigitalBrain.Runtime.Http.Surface;

namespace DigitalBrain.Runtime.Http.Tests.Unit;

public sealed class ReturnUrlValidatorFacts
{
    [Fact]
    public void ReturnUrlOutsideAllowedOriginsIsRejected()
    {
        var validator = new ReturnUrlValidator(["https://app.example.com"]);

        Assert.True(validator.IsAllowed("/"));
        Assert.True(validator.IsAllowed("/portal/home"));
        Assert.True(validator.IsAllowed("https://app.example.com/callback"));
        Assert.False(validator.IsAllowed("https://evil.example.org/callback"));
        Assert.False(validator.IsAllowed("//evil.example.org"));
        Assert.False(validator.IsAllowed("/\\evil"));
        Assert.False(validator.IsAllowed("http://app.example.com/callback"));
        Assert.False(validator.IsAllowed("https://user@app.example.com/callback"));
    }
}
