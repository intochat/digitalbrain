using DigitalBrain.TestKit;
using Xunit;

namespace DigitalBrain.Windows.Tests;

// Closes a pre-existing zero-coverage gap: WingetNeuron had no test before this plan.
// Only read-only operations (List/Search) run for real — Install/UpgradeAll mutate the host
// and are intentionally not exercised here.
// winget itself only exists on Windows, so these skip (rather than fail) on the ubuntu-latest
// CI/deploy runners; they still run for real on Windows dev boxes and any windows-* runner.
public class WingetNeuronTests : NeuronTestBase
{
    [SkippableFact]
    public async Task List_Returns_Zero_Exit_Code()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "winget is a Windows-only tool.");

        var winget = Grain<IWingetNeuron>("winget-test");
        var result = await winget.ListAsync();
        Assert.Equal(0, result.ExitCode);
    }

    [SkippableFact]
    public async Task Search_Returns_Zero_Exit_Code()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "winget is a Windows-only tool.");

        var winget = Grain<IWingetNeuron>("winget-search-test");
        var result = await winget.SearchAsync("git");
        Assert.Equal(0, result.ExitCode);
    }
}
