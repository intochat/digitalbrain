using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Xunit;

namespace DigitalBrain.HostTests;

public sealed class PublishManifest
{
    private const string SyntheticKey = "synthetic-key";

    [Fact]
    public async Task NoSecretValueReachesThePublishedManifest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var manifestPath = Path.Combine(Path.GetTempPath(), $"digitalbrain-manifest-{Guid.NewGuid():n}.json");

        try
        {
            var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.DigitalBrain_TestingAppHost>(
                ["--operation", "publish", "--publisher", "manifest", "--output-path", manifestPath],
                cancellationToken);

            await using (var app = await appHost.BuildAsync(cancellationToken))
            {
                await app.RunAsync(cancellationToken);
            }

            var manifest = await File.ReadAllTextAsync(manifestPath, cancellationToken);

            Assert.DoesNotContain(SyntheticKey, manifest, StringComparison.OrdinalIgnoreCase);

            using var document = JsonDocument.Parse(manifest);
            var key = document.RootElement.GetProperty("resources").GetProperty("openai-key");

            Assert.Equal("parameter.v0", key.GetProperty("type").GetString());
            Assert.True(key.GetProperty("inputs").GetProperty("value").GetProperty("secret").GetBoolean());
        }
        finally
        {
            File.Delete(manifestPath);
        }
    }
}
