using DigitalBrain.Coding;
using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class CodeDraftNeuronFacts
{
    [Fact]
    public async Task NeuronChecksTheSavedRevisionAndRecoversAfterDeactivation()
    {
        var ct = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "brain-draft-neuron", Guid.NewGuid().ToString("N"));
        try
        {
            await using var brain = await UnitTest.Create().WithModule<CodingModule>()
                .ConfigureSilo(silo => silo.Services.Configure<CodeExecutionOptions>(o =>
                {
                    o.Root = root;
                    o.ReferencePaths = [typeof(IBehavior).Assembly.Location];
                })).StartAsync(ct);
            var draft = brain.Get<ICodeDraft>("workspace/test");
            var saved = await draft.Save(new(0, Guid.NewGuid(),
                "await new Example().RunAsync(); public sealed class Example : DigitalBrain.Core.IBehavior { public Task RunAsync(CancellationToken cancellation = default) => Task.CompletedTask; }",
                "public class Tests { [Xunit.Fact] public async Task Runs() { await new Example().RunAsync(); } }", []), ct);
            var operation = Guid.NewGuid();
            await draft.Check(new(saved.Revision, operation), ct);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(45));
            CodeCheckSnapshot check;
            do { await Task.Delay(25, timeout.Token); check = await draft.ReadCheck(operation, timeout.Token); }
            while (!CodeDraftStore.IsTerminal(check.Status));
            Assert.True(check.Status == CodeCheckStatus.Passed, string.Join("\n", check.Diagnostics.Select(d => d.Message)));
            Assert.NotNull(check.Artifact);
            await brain.DeactivateAsync(draft, ct);
            Assert.Equal(saved.Source, (await draft.Read(ct)).Source);
            await draft.CancelCheck(operation, ct);
            Assert.Equal(CodeCheckStatus.Passed, (await draft.ReadCheck(operation, ct)).Status);
        }
        finally { if (Directory.Exists(root)) { Directory.Delete(root, true); } }
    }
}