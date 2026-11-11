using System.Text;
using System.Text.Json;
using DigitalBrain.Invocation;
using DigitalBrain.Testing.Views;
using DigitalBrain.UI;
using DigitalBrain.Units;
using Xunit;

namespace DigitalBrain.Testing.Tests.Unit.Views;

public sealed class ViewFixturesFacts
{
    private static PublishedView SampleView(string alias = "app.test", string text = "hello")
    {
        var view = new View(alias, new Column([new Text(text)]));
        var wire = JsonSerializer.SerializeToElement(view, RuntimeProtocol.OptionsFor(typeof(View)));
        return new PublishedView(alias, 1, wire);
    }

    [Fact]
    public async Task WritesOneFilePerScreenAlias()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"db-views-{Guid.NewGuid():N}");
        try
        {
            var view = SampleView("app.sample", "12");
            ViewFixtures.Write(view, tempDir);

            var files = Directory.GetFiles(tempDir, "*.json").Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray();
            Assert.Equal(["app-sample.json"], files);

            var content = await File.ReadAllTextAsync(Path.Combine(tempDir, "app-sample.json"), TestContext.Current.CancellationToken);
            Assert.Contains("\"value\": \"12\"", content, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task FixturesAreStableAcrossTwoRuns()
    {
        var dirA = Path.Combine(Path.GetTempPath(), $"db-views-a-{Guid.NewGuid():N}");
        var dirB = Path.Combine(Path.GetTempPath(), $"db-views-b-{Guid.NewGuid():N}");
        try
        {
            var viewA = SampleView("app.sample", "stable");
            var viewB = SampleView("app.sample", "stable");

            ViewFixtures.Write(viewA, dirA);
            ViewFixtures.Write(viewB, dirB);

            var filesA = Directory.GetFiles(dirA, "*.json").Order(StringComparer.Ordinal).ToArray();
            var filesB = Directory.GetFiles(dirB, "*.json").Order(StringComparer.Ordinal).ToArray();
            Assert.Equal(filesA.Select(Path.GetFileName), filesB.Select(Path.GetFileName));

            for (var i = 0; i < filesA.Length; i++)
            {
                var bytesA = await File.ReadAllBytesAsync(filesA[i], TestContext.Current.CancellationToken);
                var bytesB = await File.ReadAllBytesAsync(filesB[i], TestContext.Current.CancellationToken);
                Assert.Equal(bytesA, bytesB);
            }
        }
        finally
        {
            if (Directory.Exists(dirA)) { Directory.Delete(dirA, recursive: true); }
            if (Directory.Exists(dirB)) { Directory.Delete(dirB, recursive: true); }
        }
    }

    [Fact]
    public async Task VerifyFailsOnADifferentView()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"db-views-diff-{Guid.NewGuid():N}");
        try
        {
            var view = SampleView("app.sample", "12");
            ViewFixtures.Write(view, tempDir);

            var targetFile = Path.Combine(tempDir, "app-sample.json");
            var content = await File.ReadAllTextAsync(targetFile, Encoding.UTF8, TestContext.Current.CancellationToken);
            var mutated = content.Replace("\"value\": \"12\"", "\"value\": \"99\"", StringComparison.Ordinal);
            await File.WriteAllTextAsync(targetFile, mutated, new UTF8Encoding(false), TestContext.Current.CancellationToken);

            Environment.SetEnvironmentVariable("DIGITALBRAIN_UPDATE_VIEWS", null);
            var ex = Assert.Throws<ViewFixtureMismatchException>(() => ViewFixtures.Verify(view, tempDir));
            Assert.Contains("app.sample", ex.Message, StringComparison.Ordinal);
            Assert.Contains("99", ex.Message, StringComparison.Ordinal);
            Assert.Contains("12", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task VerifyRewritesWhenAsked()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"db-views-rewrite-{Guid.NewGuid():N}");
        var previous = Environment.GetEnvironmentVariable("DIGITALBRAIN_UPDATE_VIEWS");
        try
        {
            var view = SampleView("app.sample", "12");
            ViewFixtures.Write(view, tempDir);

            var targetFile = Path.Combine(tempDir, "app-sample.json");
            var original = await File.ReadAllTextAsync(targetFile, Encoding.UTF8, TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(targetFile, original.Replace("\"12\"", "\"99\"", StringComparison.Ordinal), new UTF8Encoding(false), TestContext.Current.CancellationToken);

            Environment.SetEnvironmentVariable("DIGITALBRAIN_UPDATE_VIEWS", "1");
            ViewFixtures.Verify(view, tempDir);

            var rewritten = await File.ReadAllTextAsync(targetFile, Encoding.UTF8, TestContext.Current.CancellationToken);
            Assert.Equal(original, rewritten);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DIGITALBRAIN_UPDATE_VIEWS", previous);
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void Slug_NormalizesKebabCase()
    {
        Assert.Equal("hello-world", ViewFixtures.Slug("Hello World!"));
        Assert.Equal("app-calculator", ViewFixtures.Slug("app.calculator"));
    }
}
