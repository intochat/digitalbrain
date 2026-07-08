namespace DigitalBrain.Tests.E2E;

internal static class E2EPrerequisites
{
    public const string EnableColdStartVariable = "DIGITALBRAIN_RUN_E2E";

    public static string SkipReason =>
        $"E2E AppHost cold start is opt-in. Start a local kernel on {DigitalBrainAppHostFixture.WarmClusterWebUrl} or set {EnableColdStartVariable}=true.";

    public static bool ColdStartEnabled =>
        IsEnabled(Environment.GetEnvironmentVariable(EnableColdStartVariable));

    private static bool IsEnabled(string? value) =>
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "1", StringComparison.OrdinalIgnoreCase);
}
