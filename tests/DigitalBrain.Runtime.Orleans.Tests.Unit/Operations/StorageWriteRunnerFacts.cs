using DigitalBrain.Capabilities.Storage;
using DigitalBrain.Operations;
using DigitalBrain.Testing.Conformance.Storage;

namespace DigitalBrain.Runtime.Orleans.Tests.Unit.Operations;

// D-04, T-05-11-01: the write runner shared by the runtime operation entity and TestOS, on one InMemoryStorage connection.
public sealed class StorageWriteRunnerFacts
{
    private readonly InMemoryStorage _a = new("a");

    private StorageWriteRunner Runner() => new Connections().Add("a", _a).Runner();

    [Fact]
    public async Task Create_folder_succeeds_reports_progress_and_the_folder_exists()
    {
        using var operation = new RecordingOperation();
        var result = await Runner().Run(WritePlan.CreateFolder(_a.Root, "New"), operation);

        Assert.IsType<OperationResult.Succeeded>(result);
        Assert.NotEmpty(operation.Reports);
        Assert.Equal(1d, operation.Reports[^1]);
        Assert.Equal(ItemKind.Folder, (await _a.Named(_a.Root, "New"))!.Kind);
    }

    [Fact]
    public async Task A_dry_run_returns_one_planned_step_per_primitive_action_and_changes_nothing()
    {
        _a.AddFile(_a.Root, "x.txt", Trees.Bytes("x"));
        using var operation = new RecordingOperation();
        var result = await Runner().Run(WritePlan.CreateFolder(_a.Root, "New") with { DryRun = true }, operation);

        var planned = Assert.IsType<OperationResult.Planned>(result);
        Assert.Equal([$"create-folder a:{_a.Root.Value}/New"], planned.Steps);
        Assert.Equal(["x.txt"], (await _a.Children(_a.Root)).Select(item => item.Name));
    }

    [Fact]
    public async Task Writing_onto_an_existing_name_fails_with_conflict_by_default_and_leaves_the_content_unchanged()
    {
        var file = _a.AddFile(_a.Root, "x.txt", Trees.Bytes("original"));
        using var operation = new RecordingOperation();
        var result = await Runner().Run(WritePlan.WriteFile(_a.Root, "x.txt", Trees.Bytes("replaced")), operation);

        Assert.Equal("conflict", Assert.IsType<OperationResult.Failed>(result).Code);
        Assert.Equal("original", await _a.Text(file));
        Assert.Single(await _a.Children(_a.Root));
    }

    [Fact]
    public async Task Rename_policy_picks_the_first_free_numbered_name_and_overwrite_replaces_only_when_explicit()
    {
        var file = _a.AddFile(_a.Root, "x.txt", Trees.Bytes("original"));
        using (var operation = new RecordingOperation())
        {
            var renamed = await Runner().Run(WritePlan.WriteFile(_a.Root, "x.txt", Trees.Bytes("second"), ConflictPolicy.Rename), operation);
            Assert.IsType<OperationResult.Succeeded>(renamed);
        }
        Assert.Equal("second", await _a.Text((await _a.Named(_a.Root, "x (1).txt"))!.Id));
        Assert.Equal("original", await _a.Text(file));

        using (var operation = new RecordingOperation())
        {
            var overwritten = await Runner().Run(WritePlan.WriteFile(_a.Root, "x.txt", Trees.Bytes("third"), ConflictPolicy.Overwrite), operation);
            Assert.IsType<OperationResult.Succeeded>(overwritten);
        }
        Assert.Equal("third", await _a.Text((await _a.Named(_a.Root, "x.txt"))!.Id));
    }

    [Fact]
    public async Task A_connection_that_cannot_write_ends_failed_unsupported()
    {
        var readOnly = new InMemoryStorage("ro", flags: ItemFlags.Read | ItemFlags.Search);
        using var operation = new RecordingOperation();
        var result = await new Connections().Add("ro", readOnly).Runner().Run(WritePlan.CreateFolder(readOnly.Root, "New"), operation);

        Assert.Equal("unsupported", Assert.IsType<OperationResult.Failed>(result).Code);
    }

    [Fact]
    public void Free_names_count_from_one_and_keep_the_extension()
    {
        Assert.Equal("x.txt", StorageWriteRunner.FreeName("x.txt", ["y.txt"]));
        Assert.Equal("x (1).txt", StorageWriteRunner.FreeName("x.txt", ["X.TXT"]));
        Assert.Equal("x (2).txt", StorageWriteRunner.FreeName("x.txt", ["x.txt", "x (1).txt"]));
        Assert.Equal("Folder (1)", StorageWriteRunner.FreeName("Folder", ["Folder"]));
    }

    [Fact]
    public async Task Cancel_during_a_hundred_step_copy_ends_cancelled_with_no_progress_after_the_cancel()
    {
        var b = new InMemoryStorage("b");
        var folder = _a.AddFolder(_a.Root, "Many");
        for (var i = 0; i < 100; i++) { _a.AddFile(folder, $"f{i:D3}.txt", Trees.Bytes($"file {i}")); }
        using var operation = new RecordingOperation { CancelWhen = (_, count) => count == 10 };

        var result = await new Connections().Add("a", _a).Add("b", b).Runner().Run(WritePlan.Copy(folder, b.Root), operation);

        Assert.IsType<OperationResult.Cancelled>(result);
        Assert.Equal(10, operation.Reports.Count);
        Assert.Equal(0, operation.ReportsAfterCancel);
        Assert.Empty(await b.Children(b.Root));
        Assert.Equal(100, (await _a.Children(folder)).Count);
    }
}
