using DigitalBrain.Coding;
using Xunit;

namespace DigitalBrain.Tests;

internal sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) { Skip = "Windows job objects only."; }
    }
}

internal sealed class WindowsTheoryAttribute : TheoryAttribute
{
    public WindowsTheoryAttribute()
    {
        if (!OperatingSystem.IsWindows()) { Skip = "Windows job objects only."; }
    }
}

public sealed class ContainedProcessFacts
{
    [WindowsFact]
    public async Task ExcessOutputIsDrainedButBounded()
    {
        var result = await new ContainedProcessRunner().RunAsync(PowerShell,
            ["-NoProfile", "-Command", "[Console]::Write('x' * 100000)"], Path.GetTempPath(), TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(65536, result.Output.Length);
    }

    [WindowsFact]
    public async Task RootCompletionAlsoTerminatesDescendants()
    {
        var runner = new ContainedProcessRunner();
        var result = await runner.RunAsync(PowerShell,
            ["-NoProfile", "-Command", "[Console]::WriteLine((Start-Process -FilePath \"$PSHOME\\powershell.exe\" -ArgumentList '-NoProfile -Command Start-Sleep -Seconds 60' -WindowStyle Hidden -PassThru).Id)"],
            Path.GetTempPath(), TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(0, result.ExitCode);
        var id = int.Parse(result.Output.Trim());
        try
        {
            using var child = System.Diagnostics.Process.GetProcessById(id);
            await child.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
        catch (ArgumentException) { /* Already reaped by the OS. */ }
    }

    [WindowsFact]
    public async Task CapturesOutputWithoutInheritingApplicationSecrets()
    {
        Environment.SetEnvironmentVariable("BRAIN_TEST_PRIVATE_VALUE", "must-not-inherit");
        try
        {
            var runner = new ContainedProcessRunner();
            var result = await runner.RunAsync(PowerShell,
                ["-NoProfile", "-Command", "[Console]::WriteLine([Environment]::GetEnvironmentVariable('BRAIN_TEST_PRIVATE_VALUE'))"], Path.GetTempPath(), TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.True(result.ExitCode == 0, result.Error);
            Assert.DoesNotContain("must-not-inherit", result.Output);
        }
        finally { Environment.SetEnvironmentVariable("BRAIN_TEST_PRIVATE_VALUE", null); }
    }

    [WindowsFact]
    public async Task DeadlineTerminatesTheContainedProcess()
    {
        var runner = new ContainedProcessRunner();
        var result = await runner.RunAsync(PowerShell,
            ["-NoProfile", "-Command", "Start-Sleep -Seconds 30"], Path.GetTempPath(), TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken);
        Assert.True(result.TimedOut);
        Assert.True(result.Duration < TimeSpan.FromSeconds(5));
    }

    [WindowsFact]
    public async Task ChildInheritsTheAmbientW3cTraceContext()
    {
        using var activity = new System.Diagnostics.Activity("propagation-probe").Start();
        var runner = new ContainedProcessRunner();
        var result = await runner.RunAsync(PowerShell,
            ["-NoProfile", "-Command", "[Console]::WriteLine([Environment]::GetEnvironmentVariable('TRACEPARENT'))"], Path.GetTempPath(), TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.True(result.ExitCode == 0, result.Error);
        var expected = "00-" + activity.TraceId.ToHexString() + "-" + activity.SpanId.ToHexString() + "-00";
        Assert.Equal(expected, result.Output.Trim());
    }

    private static string PowerShell => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
}