using System.Text.Json;
using DigitalBrain.Testing;
using DigitalBrain.UI;
using DigitalBrain.Abstractions.Identity;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class TableNeuronFacts
{
    private static JsonElement Cell(object? value) => JsonSerializer.SerializeToElement(value);
    private static CreateTable Example() => new("Scores",
        [new("name", "Name", "text"), new("score", "Score", "number")],
        [new("a", [Cell("Ada"), Cell(2)]), new("b", [Cell("Bob"), Cell(10)]), new("c", [Cell("Cy"), Cell(null)])]);

    [Fact]
    public async Task Malformed_direct_command_has_terminal_invalid_outcome_and_preserves_revision()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([typeof(UIModule)]) });
        var tables = new TableService(brain.Grains);
        var saved = await tables.CreateAsync(Example(), TestContext.Current.CancellationToken);
        var grain = brain.Grains.GetGrain<ITable>(new NeuronId(UIVocabulary.TableType, saved.Id).ToGrainId());
        var command = new UpdateTableCommand(CommandId.New(), null!);
        await grain.Update(command, TestContext.Current.CancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        TableOperationResult? result;
        do
        {
            await Task.Delay(25, timeout.Token);
            result = await grain.ReadOperation(new(command.Id));
        } while (result is null);
        Assert.Equal("invalid", result.Status);
        Assert.Equal(saved.Revision, (await tables.ReadAsync(saved.Id, cancellationToken: TestContext.Current.CancellationToken)).Revision);
    }

    [Fact]
    public async Task Numeric_filters_sort_and_reset_preserve_original_rows()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([typeof(UIModule)]) });
        var tables = new TableService(brain.Grains);
        var initial = await tables.CreateAsync(Example(), cancellationToken: TestContext.Current.CancellationToken);
        var filtered = await tables.UpdateAsync(initial.Id, new(initial.Revision,
            [new("score", "gt", Cell(2))], new("score", true), ["name"]), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("b", Assert.Single(filtered.Rows).Id);
        Assert.Equal(3, filtered.TotalRows);
        Assert.Equal(1, filtered.FilteredRows);
        var reset = await tables.UpdateAsync(initial.Id, new(filtered.Revision, [], new("score"), ["name", "score"]), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(["c", "a", "b"], reset.Rows.Select(row => row.Id));
        var page = await tables.ReadAsync(initial.Id, 1, 1, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("a", Assert.Single(page.Rows).Id);
        Assert.Equal(3, page.FilteredRows);
        Assert.Empty((await tables.ReadAsync(initial.Id, 100, 10, cancellationToken: TestContext.Current.CancellationToken)).Rows);
        await Assert.ThrowsAsync<TableValidationException>(() => tables.ReadAsync(initial.Id, -1, cancellationToken: TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<TableValidationException>(() => tables.ReadAsync(initial.Id, 0, 201, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Competing_revision_writes_have_one_success_and_one_conflict()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([typeof(UIModule)]) });
        var tables = new TableService(brain.Grains);
        var initial = await tables.CreateAsync(Example(), cancellationToken: TestContext.Current.CancellationToken);
        var attempts = await Task.WhenAll(Enumerable.Range(0, 2).Select(async index =>
        {
            try { await tables.UpdateAsync(initial.Id, new(initial.Revision, [], new("score", index == 0), ["score"]), cancellationToken: TestContext.Current.CancellationToken); return true; }
            catch (TableRevisionConflictException) { return false; }
        }));
        Assert.Single(attempts, success => success);
        Assert.Single(attempts, success => !success);
        Assert.Equal(initial.Revision + 1, (await tables.ReadAsync(initial.Id, cancellationToken: TestContext.Current.CancellationToken)).Revision);
    }

    [Fact]
    public async Task Saved_table_and_discovery_survive_cold_storage_reload()
    {
        var directory = Path.Combine(Path.GetTempPath(), "digitalbrain-tables", Guid.NewGuid().ToString("N"));
        string id;
        await using (var brain = await BrainSimulation.StartAsync(new() { Modules = new([typeof(UIModule)]), PersistenceDirectory = directory }))
        {
            var tables = new TableService(brain.Grains);
            var initial = await tables.CreateAsync(Example(), cancellationToken: TestContext.Current.CancellationToken);
            id = initial.Id;
            await tables.UpdateAsync(id, new(initial.Revision, [new("score", "gte", Cell(10))], null, ["name"]), cancellationToken: TestContext.Current.CancellationToken);
        }
        await using (var brain = await BrainSimulation.StartAsync(new() { Modules = new([typeof(UIModule)]), PersistenceDirectory = directory }))
        {
            var tables = new TableService(brain.Grains);
            Assert.Equal(id, Assert.Single(await tables.ListAsync(cancellationToken: TestContext.Current.CancellationToken)).Id);
            var saved = await tables.ReadAsync(id, cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(2, saved.Revision);
            Assert.Equal("b", Assert.Single(saved.Rows).Id);
            Assert.Equal(3, saved.TotalRows);
            Assert.Equal(["name"], saved.VisibleColumns);
        }
    }

    [Fact]
    public async Task Invalid_cells_filters_and_schema_are_rejected_without_state_changes()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([typeof(UIModule)]) });
        var tables = new TableService(brain.Grains);
        await Assert.ThrowsAsync<TableValidationException>(() => tables.CreateAsync(Example() with { Rows = [new("bad", [Cell("Bad"), Cell("10")])] }, cancellationToken: TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<TableValidationException>(() => tables.CreateAsync(Example() with { Columns = [new("x", "X", "date")], Rows = [new("bad", [Cell("2026-02-30")])] }, cancellationToken: TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<TableValidationException>(() => tables.CreateAsync(Example() with { Columns = [new("x", "X", "text"), new("x", "X", "text")] }, cancellationToken: TestContext.Current.CancellationToken));
        var saved = await tables.CreateAsync(Example(), cancellationToken: TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<TableValidationException>(() => tables.UpdateAsync(saved.Id, new(saved.Revision, [new("score", "contains", Cell(1))], null, ["score"]), cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(saved.Revision, (await tables.ReadAsync(saved.Id, cancellationToken: TestContext.Current.CancellationToken)).Revision);
        var nulls = await tables.UpdateAsync(saved.Id, new(saved.Revision, [new("score", "isNull")], null, ["score"]), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("c", Assert.Single(nulls.Rows).Id);
    }
}
