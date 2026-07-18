using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Ino.Aspire.Hosting;

// Stamps the test-mode marker into a silo's IConfiguration via Aspire's
// WithEnvironment chain. Read on the silo side by AddInoChatClients (and
// any other test-aware code path) through IConfiguration[TestModeConfigKey].
//
// The wire form INO_TEST_MODE / Ino:Mode is intentional: it flows across
// the AppHost-to-silo process boundary as an env var (which is the only
// channel Aspire has) but consumers always go through IConfiguration so
// nobody has to know about Environment.GetEnvironmentVariable. Placing the
// marker on the resource here, not in the test fixture, means "this is a
// test" is a property of the AppHost we booted — not ambient process state
// the fixture mutated behind everyone's back.
public static class InoTestModeExtensions
{
    public const string TestModeConfigKey = "Ino:Mode";
    public const string TestModeEnvVar = "Ino__Mode";
    public const string TestingValue = "Testing";

    public static IResourceBuilder<T> WithInoTestMode<T>(this IResourceBuilder<T> resource)
        where T : IResourceWithEnvironment =>
        resource.WithEnvironment(TestModeEnvVar, TestingValue);
}
