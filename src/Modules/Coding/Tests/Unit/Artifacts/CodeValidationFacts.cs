using DigitalBrain.Coding;
using DigitalBrain.Core;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class CodeValidationFacts
{
    [WindowsTheory]
    [InlineData("public sealed class UserTests { [Xunit.Fact] public void Fails() { Xunit.Assert.Fail(\"expected failure\"); } }", false)]
    [InlineData("public sealed class UserTests { [Xunit.Fact] public async Task Hangs() { await Task.Delay(60000); } }", true)]
    public async Task FailingOrHangingTestsCannotProduceArtifacts(string tests, bool timeout)
    {
        var ct = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "brain-validation-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var options = new CodeExecutionOptions { Root = root, ReferencePaths = [typeof(IBehavior).Assembly.Location], TestTimeout = TimeSpan.FromSeconds(2) };
            var service = new CodeValidationService(options);
            var source = "await new Example().RunAsync(); public sealed class Example : DigitalBrain.Core.IBehavior { public Task RunAsync(CancellationToken cancellation = default) => Task.CompletedTask; }";
            var operation = new CodeCheckSnapshot(Guid.NewGuid(), 1, CodeCheckStatus.Queued, [], null, DateTimeOffset.UtcNow, null, null);
            var error = await Record.ExceptionAsync(() => service.ValidateAsync(new(1, source, tests, [], null), operation, _ => Task.CompletedTask, ct));
            Assert.NotNull(error);
            if (timeout) { Assert.IsType<TimeoutException>(error); } else { Assert.IsType<CodeValidationException>(error); }
            Assert.False(Directory.Exists(Path.Combine(root, "artifacts")));
            Assert.Empty(Directory.GetDirectories(Path.Combine(root, "builds")));
        }
        finally { if (Directory.Exists(root)) { Directory.Delete(root, true); } }
    }

    [WindowsFact]
    public async Task RealBuildAndTestsProduceAnArtifact()
    {
        var ct = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "brain-validation-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var options = new CodeExecutionOptions { Root = root, ReferencePaths = [typeof(IBehavior).Assembly.Location] };
            var service = new CodeValidationService(options);
            var source = "await new Example().RunAsync(); public sealed class Example : DigitalBrain.Core.IBehavior { public Task RunAsync(CancellationToken cancellation = default) => Task.CompletedTask; }";
            var tests = "public sealed class UserTests { [Xunit.Fact] public async Task Runs() { await new Example().RunAsync(); } }";
            var operation = new CodeCheckSnapshot(Guid.NewGuid(), 1, CodeCheckStatus.Queued, [], null, DateTimeOffset.UtcNow, null, null);
            var result = await service.ValidateAsync(new(1, source, tests, [], null), operation, _ => Task.CompletedTask, ct);
            Assert.Equal(CodeCheckStatus.Passed, result.Status);
            Assert.NotNull(result.Artifact);
            Assert.Equal(2, result.Tests!.Passed);
            var artifact = await service.Store().OpenVerifiedAsync(result.Artifact, ct);
            Assert.True(File.Exists(Path.Combine(artifact.LaunchDirectory, artifact.EntryAssembly)));
        }
        finally { if (Directory.Exists(root)) { Directory.Delete(root, true); } }
    }
}