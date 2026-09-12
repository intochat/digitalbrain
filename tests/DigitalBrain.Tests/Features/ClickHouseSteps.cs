using System.Text.Json;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.ClickHouse;
using DigitalBrain.Testing;
using DigitalBrain.UI;
using Orleans.Runtime;
using Reqnroll;
using Xunit;

namespace DigitalBrain.Tests;

[Binding]
public sealed class ClickHouseSteps(BrainWorld world)
{
    private ClickHouseSchema? _schema;
    private ClickHouseQueryResult? _queryResult;
    private ClickHouseQueryException? _queryError;

    [Given("a running brain with the ClickHouse module")]
    public async Task StartClickHouse()
        => world.Simulation = await BrainSimulation.StartAsync(new()
        {
            Modules = new([typeof(UIModule), typeof(ClickHouseModule)]),
        });

    [When(@"""(.*)"" reads the ClickHouse schema")]
    public async Task ReadSchema(string principal)
    {
        AsCaller(principal);
        _schema = await ClickHouse.ReadSchema(new ReadClickHouseSchema());
    }

    [Then(@"the ClickHouse schema lists table ""(.*)"" with column ""(.*)"" of type ""(.*)""")]
    public void SchemaListsColumn(string table, string column, string tableType)
    {
        Assert.NotNull(_schema);
        Assert.Equal(ClickHouseNames.DatabaseName, _schema.Database);
        var listed = Assert.Single(_schema.Tables, entry => entry.Name == table);
        Assert.Equal(tableType, Assert.Single(listed.Columns, entry => entry.Name == column).TableType);
    }

    [When(@"""(.*)"" runs the ClickHouse query ""(.*)""")]
    public async Task RunQuery(string principal, string sql)
    {
        AsCaller(principal);
        _queryResult = null;
        _queryError = null;
        try
        {
            _queryResult = await ClickHouse.Query(new ClickHouseQuery(sql));
        }
        catch (ClickHouseQueryException error)
        {
            _queryError = error;
        }
    }

    [Then(@"the ClickHouse query returns (\d+) rows and is not truncated")]
    public void QueryReturns(int rows)
    {
        Assert.Null(_queryError);
        Assert.NotNull(_queryResult);
        Assert.Equal(rows, _queryResult.RowCount);
        Assert.Equal(rows, _queryResult.Rows.Count);
        Assert.False(_queryResult.Truncated);
    }

    [Then(@"the ClickHouse query column ""(.*)"" has type ""(.*)""")]
    public void QueryColumnType(string column, string tableType)
    {
        Assert.NotNull(_queryResult);
        Assert.Equal(tableType, Assert.Single(_queryResult.Columns, entry => entry.Name == column).TableType);
    }

    [Then(@"the ClickHouse query was refused with a message containing ""(.*)""")]
    public void QueryRefused(string fragment)
    {
        Assert.Null(_queryResult);
        Assert.NotNull(_queryError);
        Assert.Contains(fragment, _queryError.Message, StringComparison.Ordinal);
    }

    [When(@"""(.*)"" creates query table ""(.*)"" titled ""(.*)"" from ""(.*)""")]
    public async Task CreateQueryTable(string principal, string name, string title, string sql)
    {
        AsCaller(principal);
        var accepted = await Table(name).CreateFromQuery(new CreateQueryTableCommand(CommandId.New(), new CreateQueryTable(title, sql)));
        Assert.Equal(name, accepted.Receipt);
    }

    [Then(@"""(.*)"" waits up to (\d+) seconds until query table ""(.*)"" page (\d+) of (\d+) returns (\d+) rows of (\d+)")]
    public async Task WaitForPage(string principal, int seconds, string name, int offset, int limit, int rows, int total)
    {
        AsCaller(principal);
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        TableSnapshot? page;
        while (true)
        {
            page = await Table(name).Read(new ReadTable(offset, limit));
            if ((page is not null && page.Rows.Count == rows && page.TotalRows == total) || DateTime.UtcNow >= deadline)
            {
                break;
            }

            await Task.Delay(50);
        }

        Assert.NotNull(page);
        Assert.Equal(rows, page.Rows.Count);
        Assert.Equal(total, page.TotalRows);
        Assert.Equal(offset, page.Offset);
        Assert.Equal(limit, page.Limit);
    }

    [Then(@"query table ""(.*)"" page (\d+) of (\d+) returns (\d+) rows")]
    public async Task PageReturns(string name, int offset, int limit, int rows)
    {
        var page = await Table(name).Read(new ReadTable(offset, limit));
        Assert.NotNull(page);
        Assert.Equal(rows, page.Rows.Count);
        Assert.All(page.Rows, row => Assert.Equal(page.Columns.Count, row.Cells.Count));
    }

    [When(@"""(.*)"" filters query table ""(.*)"" where ""(.*)"" ""(.*)"" (\d+)")]
    public async Task Filter(string principal, string name, string column, string @operator, int value)
    {
        AsCaller(principal);
        var current = await Table(name).Read(new ReadTable());
        Assert.NotNull(current);
        var command = new UpdateTableCommand(CommandId.New(), new UpdateTableView(current.Revision,
            [new TableFilter(column, @operator, JsonSerializer.SerializeToElement(value))], null, current.VisibleColumns));
        await Table(name).Update(command);
        Assert.Equal("applied", (await WaitForOperation(name, command.Id)).Status);
    }

    [Then(@"query table ""(.*)"" has (\d+) filtered rows of (\d+) at revision (\d+)")]
    public async Task FilteredRows(string name, int filtered, int total, long revision)
    {
        var page = await Table(name).Read(new ReadTable(0, 50));
        Assert.NotNull(page);
        Assert.Equal(filtered, page.FilteredRows);
        Assert.Equal(total, page.TotalRows);
        Assert.Equal(revision, page.Revision);
        Assert.Equal(filtered, page.Rows.Count);
    }

    [Then(@"a stale update of query table ""(.*)"" at revision (\d+) is a conflict")]
    public async Task StaleUpdateConflicts(string name, long revision)
    {
        var current = await Table(name).Read(new ReadTable());
        Assert.NotNull(current);
        var command = new UpdateTableCommand(CommandId.New(), new UpdateTableView(revision, [], null, current.VisibleColumns));
        await Table(name).Update(command);
        var result = await WaitForOperation(name, command.Id);
        Assert.Equal("conflict", result.Status);
        Assert.Equal(current.Revision, (await Table(name).Read(new ReadTable()))!.Revision);
    }

    private async Task<TableOperationResult> WaitForOperation(string name, CommandId command)
    {
        TableOperationResult? result = null;
        await ReactionWait.UntilAsync(async () => (result = await Table(name).ReadOperation(new ReadTableOperation(command))) is not null);
        return result!;
    }

    private static void AsCaller(string principal)
        => RequestContext.Set(NeuronRequestKeys.Caller, NeuronId.Plain(principal).ToString());

    private IClickHouse ClickHouse
        => world.Brain.Grains.GetGrain<IClickHouse>(new NeuronId(ClickHouseNames.NeuronType, ClickHouseNames.DefaultNeuron).ToGrainId());

    private IClickHouseTable Table(string name)
        => world.Brain.Grains.GetGrain<IClickHouseTable>(new NeuronId(ClickHouseNames.TableType, name).ToGrainId());
}
