using DigitalBrain.Core.Enforcement;
using DigitalBrain.Core;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace DigitalBrain.Core.Tests.Unit;

public sealed class DeveloperModeFacts
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData("false", false)]
    [InlineData("False", false)]
    [InlineData("yes", false)]
    [InlineData("", false)]
    public void DeveloperModeFailsClosedOnInvalidConfigurationButDefaultsOn(string? configured, bool expected)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configured is null ? [] : [new KeyValuePair<string, string?>(DeveloperMode.Key, configured)])
            .Build();

        Assert.Equal(expected, DeveloperMode.IsEnabled(configuration));
    }
}
