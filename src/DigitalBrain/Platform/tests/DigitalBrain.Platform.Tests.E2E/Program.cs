namespace DigitalBrain.Platform.Tests.E2E;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args is ["--identity-worker", var mode])
        {
            await MigrationWorker.Run(mode);
            return Environment.ExitCode;
        }
        return await Xunit.MicrosoftTestingPlatform.TestPlatformTestFramework.RunAsync(args, SelfRegisteredExtensions.AddSelfRegisteredExtensions);
    }
}
