using DigitalBrain.AI.PersonaPlex;
using Xunit;

namespace DigitalBrain.AI.PersonaPlex.Tests;

public sealed class PersonaPlexOptionsTests
{
    [Fact]
    public void ValidateRejectsMissingTemporalAndDepformerGraphs()
    {
        var options = new PersonaPlexOptions
        {
            Enabled = true,
            ModelDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")),
        };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public void ValidateRejectsNonPositiveMaxSessions()
    {
        var options = new PersonaPlexOptions
        {
            Enabled = true,
            MaxSessions = 0,
        };

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);

        Assert.Equal("PersonaPlex requires at least one session.", exception.Message);
    }

    [Fact]
    public void ValidateAcceptsRemoteRuntimeWithoutOnnxGraphs()
    {
        var options = new PersonaPlexOptions
        {
            Enabled = true,
            UseRemoteRuntime = true,
            RuntimeEndpoint = "http://personaplex-runtime:8080",
            AdapterToken = "test-token",
        };

        options.Validate();
    }

    [Fact]
    public void ValidateRejectsRemoteRuntimeWithoutEndpoint()
    {
        var options = new PersonaPlexOptions
        {
            Enabled = true,
            UseRemoteRuntime = true,
            AdapterToken = "test-token",
        };

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.Equal("PersonaPlex remote runtime requires a non-empty RuntimeEndpoint.", exception.Message);
    }

    [Fact]
    public void ValidateRejectsRemoteRuntimeWithoutAdapterToken()
    {
        var options = new PersonaPlexOptions
        {
            Enabled = true,
            UseRemoteRuntime = true,
            RuntimeEndpoint = "http://personaplex-runtime:8080",
        };

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.Equal("PersonaPlex remote runtime requires a non-empty AdapterToken.", exception.Message);
    }
}
