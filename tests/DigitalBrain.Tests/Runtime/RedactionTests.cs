using DigitalBrain.Core.Runtime;

namespace DigitalBrain.Tests.Runtime;

public sealed class RedactionTests
{
    [Fact]
    public void Secret_summary_is_redacted() =>
        Assert.Equal("[REDACTED]", Redaction.SafeSummary("secret", Sensitivity.Secret));
}
