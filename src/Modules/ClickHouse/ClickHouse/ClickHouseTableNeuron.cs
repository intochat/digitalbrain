using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using DigitalBrain.UI;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace DigitalBrain.ClickHouse;

// An ITable whose rows live in ClickHouse. The saved view (columns, filters, sort, visible
// columns, revision) is the conversation's working set; every read compiles it into SQL.
[GrainType(ClickHouseNames.TableType)]
internal sealed class ClickHouseTableNeuron(
    NeuronRuntime runtime,
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<SnapshotEnvelope<ClickHouseTableState>> state)
    : Neuron<ClickHouseTableState>(runtime, state), IClickHouseTable
{
    private const int MaxColumns = 32;
    private const int MaxTitleLength = 200;
    private const int MaxOperations = 255;

    private IClickHouseProvider? _provider;

    private IClickHouseProvider Provider => _provider ??= ServiceProvider.GetRequiredService<IClickHouseProvider>();

    public Task<Accepted<string>> CreateFromQuery(CreateQueryTableCommand command, CancellationToken cancellationToken = default)
        => ExecuteCommandAsync(Descriptor("create-query"), command, ClickHouseJson.Default.CreateQueryTableCommand, ClickHouseJson.Default.AcceptedString, arguments =>
        {
            if (arguments.Table is null || string.IsNullOrWhiteSpace(arguments.Table.Title) || arguments.Table.Title.Length > MaxTitleLength)
            {
                throw new CommandRejectedException(arguments.Id, "title is invalid", $"Provide a title of 1–{MaxTitleLength} characters.");
            }

            try
            {
                ClickHouseQueryGuard.Validate(arguments.Table.Sql);
            }
            catch (ArgumentException error)
            {
                throw new CommandRejectedException(arguments.Id, "sql is not read-only", error.Message);
            }

            return new Accepted<string>(Id.Name, Schedule(Signal.FromJson(ClickHouseSignals.QueryTableCreating, arguments, ClickHouseJson.Default.CreateQueryTableCommand)));
        });

    public Task<Accepted<string>> Create(CreateTableCommand command, CancellationToken cancellationToken = default)
        => ExecuteCommandAsync(Descriptor("create"), command, UIJson.Default.CreateTableCommand, UIJson.Default.AcceptedString, arguments =>
            throw new CommandRejectedException(arguments.Id, "use create-query", "A ClickHouse table has no static rows; create it from a query with create-query."));

    public Task<Accepted<string>> Update(UpdateTableCommand command, CancellationToken cancellationToken = default)
        => ExecuteCommandAsync(Descriptor("update"), command, UIJson.Default.UpdateTableCommand, UIJson.Default.AcceptedString, arguments =>
            new Accepted<string>(Id.Name, Schedule(Signal.FromJson(ClickHouseSignals.QueryTableUpdating, arguments, UIJson.Default.UpdateTableCommand))));

    [ReadOnly]
    public async Task<TableSnapshot?> Read(ReadTable query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (State is not { View: { } view, BaseSql: { } baseSql } current)
        {
            return null;
        }

        TablePolicy.ValidatePage(query.Offset, query.Limit);
        var plan = new QueryPlan(baseSql, current.SourceColumns, view.Filters, view.Sort, query.Offset, query.Limit);
        QueryPage page;
        try
        {
            page = await Provider.ExecutePlanAsync(plan, CancellationToken.None).ConfigureAwait(true);
        }
        catch (ClickHouseQueryException error)
        {
            throw new TableSourceException($"ClickHouse refused the query behind table '{Id.Name}': {error.Message}");
        }
        catch (ClickHouseUnavailableException error)
        {
            throw new TableSourceException(error.Message);
        }

        return view with
        {
            Rows = page.Rows,
            TotalRows = Clamp(page.Total),
            FilteredRows = Clamp(page.Filtered),
            Offset = query.Offset,
            Limit = query.Limit,
        };
    }

    [ReadOnly]
    public Task<TableOperationResult?> ReadOperation(ReadTableOperation query)
        => Task.FromResult(State?.Operations.LastOrDefault(operation => operation.CommandId == query.CommandId));

    [ReadOnly]
    public Task<TableSummary?> ReadSummary()
        => Task.FromResult(State?.View is { } view ? new TableSummary(view.Id, view.Title, view.Revision) : null);

    protected override async Task ReceiveAsync(SignalDelivery delivery, CancellationToken cancellationToken)
    {
        var current = State ?? new ClickHouseTableState(null, null, [], []);
        var next = current;
        TableOperationResult result;
        switch (delivery.Signal.Type)
        {
            case ClickHouseSignals.QueryTableCreating:
                var create = Body(delivery, ClickHouseJson.Default.CreateQueryTableCommand);
                if (create is null || current.Operations.Any(operation => operation.CommandId == create.Id))
                {
                    return;
                }

                if (current.View is not null)
                {
                    result = new(create.Id, "conflict", "Table already exists.");
                    break;
                }

                try
                {
                    // Describing runs the query with LIMIT 0, so a wrong column or table fails here, not on first read.
                    var columns = await Provider.DescribeAsync(create.Table.Sql, cancellationToken).ConfigureAwait(true);
                    var view = View(create.Table.Title.Trim(), columns);
                    next = current with { View = view, BaseSql = create.Table.Sql, SourceColumns = columns };
                    result = new(create.Id, "applied");
                    Announce(Signal.FromJson(UIVocabulary.TableRendered, new UiCard(Id.Name, view.Title), UIJson.Default.UiCard));
                }
                catch (ClickHouseQueryException error)
                {
                    result = new(create.Id, "invalid", error.Message);
                }
                catch (TableValidationException error)
                {
                    result = new(create.Id, "invalid", error.Message);
                }
                catch (ClickHouseUnavailableException error)
                {
                    // Retrying here would create the table minutes later and hang its card on whatever
                    // turn is running then; the person asked now, so the answer is "not now".
                    result = new(create.Id, "invalid", error.Message + " Try again once ClickHouse is reachable.");
                }

                break;
            case ClickHouseSignals.QueryTableUpdating:
                var update = Body(delivery, UIJson.Default.UpdateTableCommand);
                if (update is null || current.Operations.Any(operation => operation.CommandId == update.Id))
                {
                    return;
                }

                if (current.View is not { } saved)
                {
                    result = new(update.Id, "missing");
                }
                else if (update.View is null)
                {
                    result = new(update.Id, "invalid", "View is required.");
                }
                else if (saved.Revision != update.View.ExpectedRevision)
                {
                    result = new(update.Id, "conflict", $"Expected revision {update.View.ExpectedRevision}; current revision is {saved.Revision}.");
                }
                else
                {
                    try
                    {
                        var view = TablePolicy.ValidateView(saved, update.View);
                        var changed = saved with { Revision = saved.Revision + 1, Filters = view.Filters, Sort = view.Sort, VisibleColumns = view.VisibleColumns };
                        next = current with { View = changed };
                        result = new(update.Id, "applied");
                        // The chat card re-reads the table on every offer, so the refined view shows up in place.
                        Announce(Signal.FromJson(UIVocabulary.TableRendered, new UiCard(Id.Name, changed.Title), UIJson.Default.UiCard));
                    }
                    catch (TableValidationException error)
                    {
                        result = new(update.Id, "invalid", error.Message);
                    }
                }

                break;
            default:
                return;
        }

        // Persist the exact command outcome with its resulting state, exactly like TableNeuron.
        await SaveAsync(next with { Operations = [.. current.Operations.TakeLast(MaxOperations), result] }, cancellationToken).ConfigureAwait(true);
    }

    private TableSnapshot View(string title, IReadOnlyList<ClickHouseColumn> columns)
    {
        if (columns.Count is 0 or > MaxColumns)
        {
            throw new TableValidationException($"The query must return 1–{MaxColumns} columns; it returns {columns.Count}.");
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var column in columns)
        {
            if (string.IsNullOrWhiteSpace(column.Name) || column.Name.Length > 100 || !names.Add(column.Name))
            {
                throw new TableValidationException($"Column '{column.Name}' is blank, longer than 100 characters, or repeated; alias every column with a unique name.");
            }
        }

        var tableColumns = columns.Select(column => new TableColumn(column.Name, column.Name, column.TableType)).ToArray();
        return new(Id.Name, title, 1, tableColumns, [], [], null, tableColumns.Select(column => column.Id).ToArray(), 0, 0, 0, 50);
    }

    private static int Clamp(long count) => count > int.MaxValue ? int.MaxValue : (int)count;
}
