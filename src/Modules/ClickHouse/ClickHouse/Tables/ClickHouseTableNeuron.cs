using DigitalBrain.ClickHouse.Query;
using DigitalBrain.ClickHouse.Tables.Signals;
using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace DigitalBrain.ClickHouse.Tables;

// An IClickHouseTable whose rows live in ClickHouse. The saved view (columns, filters, sort,
// visible columns, revision) is the conversation's working set; every read compiles it into SQL.
[GrainType(ClickHouseNames.TableType)]
internal sealed class ClickHouseTableNeuron(
    IClickHouseProvider provider,
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<ClickHouseTableState> state)
    : Neuron, IClickHouseTable
{
    private const int MaxColumns = 32;
    private const int MaxTitleLength = 200;

    public async Task<ClickHouseTableView> CreateFromQuery(CreateQueryTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        if (string.IsNullOrWhiteSpace(table.Title) || table.Title.Length > MaxTitleLength)
        {
            throw new ClickHouseTableValidationException($"Provide a title of 1–{MaxTitleLength} characters.");
        }

        try
        {
            ClickHouseQueryGuard.Validate(table.Sql);
        }
        catch (ArgumentException error)
        {
            throw new ClickHouseTableValidationException(error.Message);
        }

        if (state.State.View is not null)
        {
            throw new ClickHouseTableValidationException("Table already exists.");
        }

        try
        {
            // Describing runs the query with LIMIT 0, so a wrong column or table fails here, not on first read.
            var columns = await provider.DescribeAsync(table.Sql, CancellationToken.None).ConfigureAwait(true);
            var view = View(table.Title.Trim(), columns);
            state.State = new ClickHouseTableState(view, table.Sql, columns);
            await state.WriteStateAsync().ConfigureAwait(true);
            await PublishAsync(new TableRendered(this.GetPrimaryKeyString(), view.Title)).ConfigureAwait(true);
            return view;
        }
        catch (ClickHouseQueryException error)
        {
            throw new ClickHouseTableValidationException(error.Message);
        }
        catch (ClickHouseUnavailableException error)
        {
            // Retrying here would create the table minutes later and hang its card on whatever turn
            // is running then; the person asked now, so the answer is "not now".
            throw new ClickHouseTableValidationException(error.Message + " Try again once ClickHouse is reachable.");
        }
    }

    public async Task<ClickHouseTableView> Update(UpdateClickHouseTableView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (state.State.View is not { } saved)
        {
            throw new ClickHouseTableValidationException("Table does not exist.");
        }

        if (saved.Revision != view.ExpectedRevision)
        {
            throw new ClickHouseTableValidationException($"Expected revision {view.ExpectedRevision}; current revision is {saved.Revision}.");
        }

        var validated = ClickHouseTablePolicy.ValidateView(saved, view);
        var changed = saved with
        {
            Revision = saved.Revision + 1,
            Filters = validated.Filters,
            Sort = validated.Sort,
            VisibleColumns = validated.VisibleColumns,
        };
        state.State = state.State with { View = changed };
        await state.WriteStateAsync().ConfigureAwait(true);
        // The chat card re-reads the table on every offer, so the refined view shows up in place.
        await PublishAsync(new TableRendered(this.GetPrimaryKeyString(), changed.Title)).ConfigureAwait(true);
        return changed;
    }

    [ReadOnly]
    public async Task<ClickHouseTableSnapshot?> Read(ReadClickHouseTable query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (state.State is not { View: { } view, BaseSql: { } baseSql } current)
        {
            return null;
        }

        ClickHouseTablePolicy.ValidatePage(query.Offset, query.Limit);
        var plan = new QueryPlan(baseSql, current.SourceColumns, view.Filters, view.Sort, query.Offset, query.Limit);
        QueryPage page;
        try
        {
            page = await provider.ExecutePlanAsync(plan, CancellationToken.None).ConfigureAwait(true);
        }
        catch (ClickHouseQueryException error)
        {
            throw new ClickHouseTableSourceException($"ClickHouse refused the query behind table '{this.GetPrimaryKeyString()}': {error.Message}");
        }
        catch (ClickHouseUnavailableException error)
        {
            throw new ClickHouseTableSourceException(error.Message);
        }

        return new ClickHouseTableSnapshot(
            view.Id, view.Title, view.Revision, view.Columns, page.Rows, view.Filters, view.Sort, view.VisibleColumns,
            Clamp(page.Total), Clamp(page.Filtered), query.Offset, query.Limit);
    }

    [ReadOnly]
    public Task<ClickHouseTableSummary?> ReadSummary()
        => Task.FromResult(state.State.View is { } view ? new ClickHouseTableSummary(view.Id, view.Title, view.Revision) : null);

    public async Task<ClickHouseTableSummary?> Render()
    {
        if (state.State.View is not { } view)
        {
            return null;
        }

        await PublishAsync(new TableRendered(this.GetPrimaryKeyString(), view.Title)).ConfigureAwait(true);
        return new ClickHouseTableSummary(view.Id, view.Title, view.Revision);
    }

    private ClickHouseTableView View(string title, IReadOnlyList<ClickHouseColumn> columns)
    {
        if (columns.Count is 0 or > MaxColumns)
        {
            throw new ClickHouseTableValidationException($"The query must return 1–{MaxColumns} columns; it returns {columns.Count}.");
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var column in columns)
        {
            if (string.IsNullOrWhiteSpace(column.Name) || column.Name.Length > 100 || !names.Add(column.Name))
            {
                throw new ClickHouseTableValidationException($"Column '{column.Name}' is blank, longer than 100 characters, or repeated; alias every column with a unique name.");
            }
        }

        var tableColumns = columns.Select(column => new ClickHouseTableColumn(column.Name, column.Name, column.TableType)).ToArray();
        return new ClickHouseTableView(this.GetPrimaryKeyString(), title, 1, tableColumns, [], null, tableColumns.Select(column => column.Id).ToArray());
    }

    private static int Clamp(long count) => count > int.MaxValue ? int.MaxValue : (int)count;
}
