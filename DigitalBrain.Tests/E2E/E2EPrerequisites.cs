namespace DigitalBrain.Tests.E2E;

// Gates the real-stack E2E tests (real Aspire-hosted kernel + real gRPC wire) so they only run
// deliberately, not on every `dotnet test`.
public static class E2EPrerequisites
{
    public static bool OptedIn =>
        string.Equals(Environment.GetEnvironmentVariable("RUN_REAL_STACK_E2E"), "true", StringComparison.OrdinalIgnoreCase);

    public static void RequireRealStackE2E()
    {
        Skip.IfNot(OptedIn, "Set RUN_REAL_STACK_E2E=true to run the real-stack E2E tests.");
    }
}
