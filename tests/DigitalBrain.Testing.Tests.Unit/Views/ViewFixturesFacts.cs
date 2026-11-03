using System.Text;
using DigitalBrain;
using DigitalBrain.Examples.Calculator;
using DigitalBrain.Testing;
using DigitalBrain.Testing.Views;

namespace DigitalBrain.Testing.Tests.Unit.Views;

public sealed class ViewFixturesFacts
{
    [Fact]
    public async Task WritesOneFilePerScenario()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"db-views-{Guid.NewGuid():N}");
        try
        {
            var report = await BddRunner.VerifyAsync<CalculatorApp>(cancellationToken: TestContext.Current.CancellationToken);
            ViewFixtures.Write(report, tempDir);

            var files = Directory.GetFiles(tempDir, "*.json").Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray();
            Assert.Equal(
                [
                    "7-5-12.json",
                    "clear-resets-display-to-0.json",
                    "dividing-by-zero-is-rejected.json",
                    "opening-shows-0.json",
                ],
                files);

            var sevenPlusFive = await File.ReadAllTextAsync(Path.Combine(tempDir, "7-5-12.json"), TestContext.Current.CancellationToken);
            Assert.Contains("\"value\": \"12\"", sevenPlusFive, StringComparison.Ordinal);
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
            var reportA = await BddRunner.VerifyAsync<CalculatorApp>(cancellationToken: TestContext.Current.CancellationToken);
            var reportB = await BddRunner.VerifyAsync<CalculatorApp>(cancellationToken: TestContext.Current.CancellationToken);

            ViewFixtures.Write(reportA, dirA);
            ViewFixtures.Write(reportB, dirB);

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
            var report = await BddRunner.VerifyAsync<CalculatorApp>(cancellationToken: TestContext.Current.CancellationToken);
            ViewFixtures.Write(report, tempDir);

            var targetFile = Path.Combine(tempDir, "7-5-12.json");
            var content = await File.ReadAllTextAsync(targetFile, Encoding.UTF8, TestContext.Current.CancellationToken);
            var mutated = content.Replace("\"value\": \"12\"", "\"value\": \"99\"", StringComparison.Ordinal);
            await File.WriteAllTextAsync(targetFile, mutated, new UTF8Encoding(false), TestContext.Current.CancellationToken);

            Environment.SetEnvironmentVariable("DIGITALBRAIN_UPDATE_VIEWS", null);
            var ex = Assert.Throws<ViewFixtureMismatchException>(() => ViewFixtures.Verify(report, tempDir));
            Assert.Contains("7 + 5 = 12", ex.Message, StringComparison.Ordinal);
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
            var report = await BddRunner.VerifyAsync<CalculatorApp>(cancellationToken: TestContext.Current.CancellationToken);
            ViewFixtures.Write(report, tempDir);

            var targetFile = Path.Combine(tempDir, "7-5-12.json");
            var original = await File.ReadAllTextAsync(targetFile, Encoding.UTF8, TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(targetFile, original.Replace("\"12\"", "\"99\"", StringComparison.Ordinal), new UTF8Encoding(false), TestContext.Current.CancellationToken);

            Environment.SetEnvironmentVariable("DIGITALBRAIN_UPDATE_VIEWS", "1");
            ViewFixtures.Verify(report, tempDir);

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
}
