using System.Collections.Concurrent;
using DigitalBrain.Capabilities.Storage;
using DigitalBrain.Operations;
using DigitalBrain.Testing.Conformance.Storage;

namespace DigitalBrain.Runtime.Orleans.Tests.Unit.Operations;

// D-04, T-05-11-02: a move between two connections copies, verifies, then deletes; the source survives every failure and cancel.
public sealed class CrossConnectionMoveFacts
{
    private readonly ConcurrentQueue<string> _log = new();
    private readonly InMemoryStorage _memoryA = new("a");
    private readonly InMemoryStorage _memoryB = new("b");
    private readonly FaultyStorage _a;
    private readonly FaultyStorage _b;

    public CrossConnectionMoveFacts()
    {
        _a = new FaultyStorage(_memoryA, _log);
        _b = new FaultyStorage(_memoryB, _log);
    }

    private StorageWriteRunner Runner() => new Connections().Add("a", _a).Add("b", _b).Runner();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SourceEditedAfterCopyIsKeptWithTheVerifiedCopy(bool hideHashes)
    {
        var source = _memoryA.AddFile(_memoryA.Root, "report.txt", Trees.Bytes("before"));
        _a.HideHashes = _b.HideHashes = hideHashes;
        using var operation = new RecordingOperation
        {
            Reported = async fraction =>
            {
                if (fraction == 0.5)
                {
                    await _memoryA.Write(WritePlan.WriteFile(_memoryA.Root, "report.txt", Trees.Bytes("edited"), ConflictPolicy.Overwrite));
                }
            },
        };

        var result = await Runner().Run(WritePlan.Move(source, _memoryB.Root), operation);

        Assert.Equal("source-changed", Assert.IsType<OperationResult.Failed>(result).Code);
        Assert.Equal(source, Assert.Single(await _memoryA.Children(_memoryA.Root)).Id);
        Assert.Equal("edited", await _memoryA.Text(source));
        Assert.Equal("before", await _memoryB.Text(Assert.Single(await _memoryB.Children(_memoryB.Root)).Id));
        Assert.DoesNotContain("a:Write:Delete", _log);
    }

    [Fact]
    public async Task UnreadableSourceDuringFinalCheckKeepsBothCopies()
    {
        var source = _memoryA.AddFile(_memoryA.Root, "report.txt", Trees.Bytes("before"));
        _a.HideHashes = _b.HideHashes = true;
        using var operation = new RecordingOperation
        {
            Reported = fraction =>
            {
                if (fraction == 0.5) { _a.FailReads = true; }
                return Task.CompletedTask;
            },
        };

        var result = await Runner().Run(WritePlan.Move(source, _memoryB.Root), operation);

        Assert.Equal("source-check-failed", Assert.IsType<OperationResult.Failed>(result).Code);
        Assert.Equal(source, Assert.Single(await _memoryA.Children(_memoryA.Root)).Id);
        Assert.Equal("before", await _memoryA.Text(source));
        Assert.Equal("before", await _memoryB.Text(Assert.Single(await _memoryB.Children(_memoryB.Root)).Id));
        Assert.DoesNotContain("a:Write:Delete", _log);
    }

    [Fact]
    public async Task DescendantAddedAfterCopyKeepsTheSourceFolderAndVerifiedCopy()
    {
        var source = _memoryA.AddFolder(_memoryA.Root, "Project");
        _memoryA.AddFile(source, "original.txt", Trees.Bytes("original"));
        using var operation = new RecordingOperation
        {
            Reported = fraction =>
            {
                if (fraction == 2d / 3) { _memoryA.AddFile(source, "new.txt", Trees.Bytes("new work")); }
                return Task.CompletedTask;
            },
        };

        var result = await Runner().Run(WritePlan.Move(source, _memoryB.Root), operation);

        Assert.Equal("source-changed", Assert.IsType<OperationResult.Failed>(result).Code);
        Assert.Equal(source, Assert.Single(await _memoryA.Children(_memoryA.Root)).Id);
        Assert.Equal("new work", await _memoryA.Text((await _memoryA.Named(source, "new.txt"))!.Id));
        var copy = Assert.Single(await _memoryB.Children(_memoryB.Root));
        Assert.Equal("original", await _memoryB.Text(Assert.Single(await _memoryB.Children(copy.Id)).Id));
        Assert.DoesNotContain("a:Write:Delete", _log);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedOrCancelledOverwritePreservesBothOriginalFiles(bool cancel)
    {
        var source = _memoryA.AddFile(_memoryA.Root, "report.txt", Trees.Bytes("source"));
        var existing = _memoryB.AddFile(_memoryB.Root, "report.txt", Trees.Bytes("original target"));
        _b.CorruptWrites = !cancel;
        using var operation = new RecordingOperation { CancelWhen = cancel ? (fraction, _) => fraction >= 0.5 : null };
        var result = await Runner().Run(WritePlan.Move(source, _memoryB.Root, ConflictPolicy.Overwrite), operation);
        Assert.False(result is OperationResult.Succeeded);
        Assert.Equal("source", await _memoryA.Text(source));
        Assert.Equal("original target", await _memoryB.Text(existing));
        Assert.Single(await _memoryB.Children(_memoryB.Root));
    }

    [Fact]
    public async Task OverwritePublishesOnlyAfterVerification()
    {
        var source = _memoryA.AddFile(_memoryA.Root, "report.txt", Trees.Bytes("source"));
        _memoryB.AddFile(_memoryB.Root, "report.txt", Trees.Bytes("original target"));
        using var operation = new RecordingOperation();
        Assert.IsType<OperationResult.Succeeded>(await Runner().Run(WritePlan.Move(source, _memoryB.Root, ConflictPolicy.Overwrite), operation));
        var target = Assert.Single(await _memoryB.Children(_memoryB.Root));
        Assert.Equal("report.txt", target.Name);
        Assert.Equal("source", await _memoryB.Text(target.Id));
        Assert.Empty(await _memoryA.Children(_memoryA.Root));
    }

    [Fact]
    public async Task VerificationStreamsWhenProvidersHaveNoComparableHash()
    {
        var bytes = new byte[900_001];
        new Random(17).NextBytes(bytes);
        var file = _memoryA.AddFile(_memoryA.Root, "large.bin", bytes);
        _a.HideHashes = _b.HideHashes = true;
        _a.RefuseWholeReads = _b.RefuseWholeReads = true;
        using var operation = new RecordingOperation();
        var result = await Runner().Run(WritePlan.Move(file, _memoryB.Root), operation);
        Assert.IsType<OperationResult.Succeeded>(result);
        Assert.Empty(await _memoryA.Children(_memoryA.Root));
        var copy = Assert.Single(await _memoryB.Children(_memoryB.Root));
        Assert.Equal(bytes, (await _memoryB.Read(copy.Id, null, TestContext.Current.CancellationToken)).Bytes.ToArray());
    }

    [Fact]
    public async Task A_move_to_another_connection_copies_equal_bytes_removes_the_source_and_succeeds()
    {
        var file = _memoryA.AddFile(_memoryA.Root, "doc.txt", Trees.Bytes("hello"));
        using var operation = new RecordingOperation();

        var result = await Runner().Run(WritePlan.Move(file, _memoryB.Root), operation);

        Assert.IsType<OperationResult.Succeeded>(result);
        Assert.Equal("hello", await _memoryB.Text((await _memoryB.Named(_memoryB.Root, "doc.txt"))!.Id));
        Assert.Empty(await _memoryA.Children(_memoryA.Root));
        Assert.Equal(["doc.txt"], (await _memoryA.Children(_memoryA.Trash)).Select(item => item.Name)); // to the trash, not gone
        Assert.Equal(1d, operation.Reports[^1]);
    }

    [Fact]
    public async Task A_verify_failure_keeps_the_source_removes_the_partial_target_and_fails_verify_failed()
    {
        var file = _memoryA.AddFile(_memoryA.Root, "doc.txt", Trees.Bytes("hello"));
        _b.CorruptWrites = true;
        using var operation = new RecordingOperation();

        var result = await Runner().Run(WritePlan.Move(file, _memoryB.Root), operation);

        Assert.Equal("verify-failed", Assert.IsType<OperationResult.Failed>(result).Code);
        Assert.Equal("hello", await _memoryA.Text(file));
        Assert.Empty(await _memoryB.Children(_memoryB.Root));
        Assert.DoesNotContain("a:Write:Delete", _log);
    }

    [Fact]
    public async Task A_name_conflict_on_the_target_fails_conflict_by_default_and_copies_nothing()
    {
        var file = _memoryA.AddFile(_memoryA.Root, "doc.txt", Trees.Bytes("mine"));
        var existing = _memoryB.AddFile(_memoryB.Root, "doc.txt", Trees.Bytes("theirs"));
        using var operation = new RecordingOperation();

        var result = await Runner().Run(WritePlan.Move(file, _memoryB.Root), operation);

        Assert.Equal("conflict", Assert.IsType<OperationResult.Failed>(result).Code);
        Assert.Equal("theirs", await _memoryB.Text(existing));
        Assert.Single(await _memoryB.Children(_memoryB.Root));
        Assert.Equal("mine", await _memoryA.Text(file));
        Assert.DoesNotContain("b:OpenWrite", _log);
    }

    [Fact]
    public async Task Cancel_after_the_copy_and_before_the_delete_keeps_the_source_removes_the_copy_and_ends_cancelled()
    {
        var file = _memoryA.AddFile(_memoryA.Root, "doc.txt", Trees.Bytes("hello"));
        // One item plus the delete: the copy reports 0.5, then the fact cancels before the delete step.
        using var operation = new RecordingOperation { CancelWhen = (fraction, _) => fraction >= 0.5 };

        var result = await Runner().Run(WritePlan.Move(file, _memoryB.Root), operation);

        Assert.IsType<OperationResult.Cancelled>(result);
        Assert.Equal("hello", await _memoryA.Text(file));
        Assert.Empty(await _memoryB.Children(_memoryB.Root));
        Assert.DoesNotContain("a:Write:Delete", _log);
    }

    [Fact]
    public async Task A_folder_move_copies_recursively_and_deletes_the_source_only_after_every_child_verified()
    {
        var folder = _memoryA.AddFolder(_memoryA.Root, "Project");
        _memoryA.AddFile(folder, "one.txt", Trees.Bytes("1"));
        var inner = _memoryA.AddFolder(folder, "Inner");
        _memoryA.AddFile(inner, "two.txt", Trees.Bytes("22"));
        using var operation = new RecordingOperation();

        var result = await Runner().Run(WritePlan.Move(folder, _memoryB.Root), operation);

        Assert.IsType<OperationResult.Succeeded>(result);
        var copied = (await _memoryB.Named(_memoryB.Root, "Project"))!;
        Assert.Equal("1", await _memoryB.Text((await _memoryB.Named(copied.Id, "one.txt"))!.Id));
        var copiedInner = (await _memoryB.Named(copied.Id, "Inner"))!;
        Assert.Equal("22", await _memoryB.Text((await _memoryB.Named(copiedInner.Id, "two.txt"))!.Id));
        Assert.Empty(await _memoryA.Children(_memoryA.Root));

        var log = _log.ToList();
        var delete = log.IndexOf("a:Write:Delete");
        Assert.True(delete > log.FindLastIndex(entry => entry == "b:Stat"), string.Join(", ", log));
        Assert.Equal(2, log.Count(entry => entry == "b:OpenWrite"));
    }

    [Fact]
    public async Task A_failed_source_delete_after_a_verified_copy_reports_the_partial_state_and_keeps_both_copies()
    {
        var file = _memoryA.AddFile(_memoryA.Root, "doc.txt", Trees.Bytes("hello"));
        _a.FailDeletes = true;
        using var operation = new RecordingOperation();

        var result = await Runner().Run(WritePlan.Move(file, _memoryB.Root), operation);

        Assert.Equal("source-delete-failed", Assert.IsType<OperationResult.Failed>(result).Code);
        Assert.Equal("hello", await _memoryA.Text(file));
        Assert.Equal("hello", await _memoryB.Text((await _memoryB.Named(_memoryB.Root, "doc.txt"))!.Id));
    }

    [Fact]
    public async Task A_cross_connection_dry_run_lists_copy_verify_and_delete_steps_and_changes_nothing()
    {
        var file = _memoryA.AddFile(_memoryA.Root, "doc.txt", Trees.Bytes("hello"));
        using var operation = new RecordingOperation();

        var result = await Runner().Run(WritePlan.Move(file, _memoryB.Root) with { DryRun = true }, operation);

        var planned = Assert.IsType<OperationResult.Planned>(result);
        Assert.Equal([$"copy a:{file.Value} -> b:doc.txt", "verify b:doc.txt", $"delete a:{file.Value} to-trash"], planned.Steps);
        Assert.Empty(await _memoryB.Children(_memoryB.Root));
        Assert.Equal("hello", await _memoryA.Text(file));
    }
}
