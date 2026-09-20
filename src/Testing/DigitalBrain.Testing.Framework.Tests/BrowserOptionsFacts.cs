using DigitalBrain.Testing.E2E;

namespace DigitalBrain.Tests;

public sealed class BrowserOptionsFacts
{
    [Fact]
    public void ExplicitHeadedUsesVisibleBrowserAndDefaultSlowMotion()
    {
        var resolved = BrowserOptionsResolver.Resolve(new() { Headless = false }, false, false);
        Assert.False(resolved.Headless);
        Assert.Equal(250, resolved.SlowMoMilliseconds);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.MaxValue)]
    public void InvalidTimeoutFailsBeforeLaunch(double milliseconds)
    {
        var timeout = milliseconds == double.MaxValue ? TimeSpan.MaxValue : TimeSpan.FromMilliseconds(milliseconds);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BrowserOptionsResolver.Resolve(new() { StartupTimeout = timeout }, false, false));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BrowserOptionsResolver.Resolve(new() { AssertionTimeout = timeout }, false, false));
    }

    [Fact]
    public void ExplicitHeadlessAndZeroDelayOverrideInteractivePreferences()
    {
        var resolved = BrowserOptionsResolver.Resolve(
            new() { Headless = true, SlowMoMilliseconds = 0 }, true, true);
        Assert.True(resolved.Headless);
        Assert.Equal(0, resolved.SlowMoMilliseconds);
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public void UnspecifiedModeUsesInteractivePreferences(bool headed, bool debugger, bool expected)
        => Assert.Equal(expected, BrowserOptionsResolver.Resolve(new(), headed, debugger).Headless);

    [Theory]
    [InlineData(-1)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void InvalidDelayFailsBeforeLaunch(float delay)
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            BrowserOptionsResolver.Resolve(new() { SlowMoMilliseconds = delay }, false, false));
}
