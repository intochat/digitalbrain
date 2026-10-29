// CA1822: the sample apps are instance classes on purpose; an [On] method is an instance member.
#pragma warning disable CA1822
using System.Diagnostics;
using DigitalBrain.Capabilities;
using DigitalBrain.Capabilities.Storage;
using DigitalBrain.UI;

namespace DigitalBrain.Testing.Tests.Unit.Baseline;

[Signal("baseline-sample.added")]
public sealed record Added(string Text) : Signal;

[Capability("storage-like", 1)]
public interface IStorageLikeForBaseline
{
    string Label { get; }
}

// Three operations; empty input to "add" gives a typed validation state, not an error.
[App("sample")]
public sealed class BaselineSample(IPresenter presenter) : App
{
    private readonly List<string> _items = [];

    [On]
    public Task Open()
    {
        Show(null);
        return Task.CompletedTask;
    }

    [On]
    public Task Add(Added signal, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(signal.Text)) { Show("Text is required."); return Task.CompletedTask; }
        _items.Add(signal.Text);
        Show(null);
        return Task.CompletedTask;
    }

    [On]
    public Task Clear()
    {
        _items.Clear();
        Show(null);
        return Task.CompletedTask;
    }

    private void Show(string? validation)
        => Present(presenter.Present,
            () => new View("Sample", new Collection([.. _items.Select((item, i) => new Entry(i.ToString(System.Globalization.CultureInfo.InvariantCulture), item, null))])),
            () => new View("Sample", new Column(validation is null ? [new Text($"{_items.Count} items")] : [new Text(validation)])));
}

[App("thrower")]
public sealed class ThrowingSample(IPresenter presenter) : App
{
    [On]
    public Task Open()
    {
        presenter.Present(null, new View("x", new Text("x")));
        throw new InvalidOperationException("boom");
    }
}

[App("needs-storage-like")]
public sealed class NeedsStorageLike(IStorageLikeForBaseline storage, IPresenter presenter) : App
{
    [On]
    public Task Open()
    {
        presenter.Present(null, new View(storage.Label, new Text(storage.Label)));
        return Task.CompletedTask;
    }
}

[App("uses-storage")]
public sealed class UsesStorage(IStorage storage, IPresenter presenter) : App
{
    [On]
    public Task Open()
    {
        presenter.Present(null, new View("Storage", new Text(storage.GetType().Name)));
        return Task.CompletedTask;
    }
}

[App("records-error")]
public sealed class RecordsError(IPresenter presenter) : App
{
    [On]
    public Task Open()
    {
        try { throw new IOException("disk gone"); }
        catch (IOException error) { RecordError(error); }
        presenter.Present(null, new View("Error", new Text("failed")));
        return Task.CompletedTask;
    }
}

[App("broken-view")]
public sealed class BrokenView : App
{
    public View Main => throw new InvalidDataException("bad row");

    [On]
    public Task Open() => Task.CompletedTask;
}

[App("silent")]
public sealed class Silent : App
{
    [On]
    public Task Open() => Task.CompletedTask;
}

public sealed class AppBaselineFacts
{
    public static IEnumerable<TheoryDataRow<AppOperationCase>> SampleOperations => AppBaseline.Cases(typeof(BaselineSample));

    [Theory]
    [MemberData(nameof(SampleOperations))]
    public Task The_sample_passes_its_baseline(AppOperationCase operation) => AppBaseline.RunAsync(operation);

    [Fact]
    public void Cases_yield_one_row_per_operation_named_app_slash_operation()
    {
        var rows = AppBaseline.Cases(typeof(BaselineSample)).ToList();
        Assert.Equal(["sample/add", "sample/clear", "sample/open"], rows.Select(row => row.TestDisplayName));
        Assert.Equal(["add", "clear", "open"], rows.Select(row => row.Data!.Operation));
        Assert.Equal("sample/add", rows[0].Data!.ToString());
    }

    [Fact]
    public async Task A_reaction_that_throws_fails_naming_the_app_and_the_operation()
    {
        var error = await Assert.ThrowsAsync<AppBaselineException>(() => AppBaseline.RunAsync(Single(typeof(ThrowingSample))));
        Assert.Contains("thrower/open", error.Message, StringComparison.Ordinal);
        Assert.Contains("boom", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_required_capability_without_a_fake_fails_loudly_naming_it()
    {
        var error = await Assert.ThrowsAsync<AppBaselineException>(() => AppBaseline.RunAsync(Single(typeof(NeedsStorageLike))));
        Assert.Contains("storage-like@1", error.Message, StringComparison.Ordinal);
        Assert.Contains("needs-storage-like/open", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_fake_passed_to_Cases_is_bound()
    {
        var row = Assert.Single(AppBaseline.Cases(typeof(NeedsStorageLike), os => os.With<IStorageLikeForBaseline>(new StorageLabel("drive"))));
        await AppBaseline.RunAsync(row.Data!);
    }

    [Fact]
    public async Task Storage_gets_a_default_fake()
    {
        Assert.Contains(CapabilityId.Parse("storage@1"), AppBaseline.DefaultFakes.Keys);
        await AppBaseline.RunAsync(Single(typeof(UsesStorage)));
    }

    [Fact]
    public async Task A_recorded_error_state_fails()
    {
        var error = await Assert.ThrowsAsync<AppBaselineException>(() => AppBaseline.RunAsync(Single(typeof(RecordsError))));
        Assert.Contains("error state", error.Message, StringComparison.Ordinal);
        Assert.Contains("disk gone", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_view_that_throws_fails()
    {
        var error = await Assert.ThrowsAsync<AppBaselineException>(() => AppBaseline.RunAsync(Single(typeof(BrokenView))));
        Assert.Contains("bad row", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_app_that_presents_nothing_fails()
    {
        var error = await Assert.ThrowsAsync<AppBaselineException>(() => AppBaseline.RunAsync(Single(typeof(Silent))));
        Assert.Contains("neither a side nor a main view", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_three_operation_app_runs_well_under_the_budget()
    {
        var watch = Stopwatch.StartNew();
        foreach (var row in AppBaseline.Cases(typeof(BaselineSample)))
        {
            await AppBaseline.RunAsync(row.Data!);
        }
        Assert.True(watch.Elapsed < AppBaseline.Budget, $"took {watch.Elapsed}");
    }

    [Fact]
    public async Task The_manifest_overload_drives_an_app_installed_by_a_delegate()
    {
        var manifest = TestOS.ManifestOf(typeof(BaselineSample));
        var rows = AppBaseline.Cases(manifest, ["Open", "add"], async os => await os.Install<BaselineSample>(), fakes: null).ToList();
        Assert.Equal(["sample/add", "sample/open"], rows.Select(row => row.TestDisplayName));
        foreach (var row in rows) { await AppBaseline.RunAsync(row.Data!); }
        Assert.Throws<InvalidOperationException>(() => AppBaseline.Cases(manifest, [], async os => await os.Install<BaselineSample>(), fakes: null));
    }

    private static AppOperationCase Single(Type app) => Assert.Single(AppBaseline.Cases(app)).Data!;

    private sealed class StorageLabel(string label) : IStorageLikeForBaseline
    {
        public string Label { get; } = label;
    }
}
