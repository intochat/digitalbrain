using DigitalBrain.Contracts;
using DigitalBrain.Core;
using DigitalBrain.Supabase.Tables;
using DigitalBrain.Supabase.Tables.Signals;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace DigitalBrain.Supabase;

// An ISupabaseTable whose rows live in Supabase. The saved view (columns, filters, sort, visible
// columns, revision) is the conversation's working set; every read compiles it into SQL.
[GrainType(SupabaseNames.TableType)]
internal sealed class SupabaseTableNeuron(
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<SupabaseTableState> state)
    : Neuron, ISupabaseTable
{
    private ISupabaseProvider? _provider;

    private ISupabaseProvider Provider => _provider ??= ServiceProvider.GetRequiredService<ISupabaseProvider>();
    private SupabaseTableState Current => state.State ?? SupabaseTableState.Empty;

    public async Task<SupabaseTableSnapshot> CreateFromQuery(CreateQueryTable request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 200)
        {
            throw new SupabaseTableValidationException("Provide a title of 1–200 characters.");
        }

        try
        {
            SupabaseQueryGuard.Validate(request.Sql);
        }
        catch (ArgumentException error)
        {
            throw new SupabaseTableValidationException(error.Message);
        }

        var current = Current;
        if (current.View is not null)
        {
            throw new SupabaseTableValidationException("Table already exists.");
        }

        IReadOnlyList<SupabaseColumn> columns;
        try
        {
            // Describing runs the query with LIMIT 0, so a wrong column or table fails here, not on first read.
            columns = await Provider.DescribeAsync(request.Sql, CancellationToken.None);
        }
        catch (SupabaseQueryException error)
        {
            throw new SupabaseTableValidationException(error.Message);
        }
        catch (SupabaseUnavailableException error)
        {
            throw new SupabaseTableSourceException(error.Message + " Try again once Supabase is reachable.");
        }

        var view = SupabaseTablePolicy.CreateView(this.GetPrimaryKeyString(), request.Title, columns);
        state.State = current with { View = view, BaseSql = request.Sql, SourceColumns = columns };
        await state.WriteStateAsync();
        await PublishAsync(new SupabaseTableChanged(view.Id, view.Title, view.Revision));
        return view;
    }

    public async Task<SupabaseTableSnapshot> UpdateView(UpdateSupabaseTableView request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var current = Current;
        if (current.View is not { } saved)
        {
            throw new SupabaseTableNotFoundException(this.GetPrimaryKeyString());
        }

        if (saved.Revision != request.ExpectedRevision)
        {
            throw new SupabaseTableRevisionConflictException($"Expected revision {request.ExpectedRevision}; current revision is {saved.Revision}.");
        }

        var normalized = SupabaseTablePolicy.ValidateView(saved, request);
        var changed = saved with
        {
            Revision = saved.Revision + 1,
            Filters = normalized.Filters,
            Sort = normalized.Sort,
            VisibleColumns = normalized.VisibleColumns,
        };
        state.State = current with { View = changed };
        await state.WriteStateAsync();
        await PublishAsync(new SupabaseTableChanged(changed.Id, changed.Title, changed.Revision));
        return changed;
    }

    [ReadOnly]
    public async Task<SupabaseTableSnapshot?> Read(ReadSupabaseTable query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (Current is not { View: { } view, BaseSql: { } baseSql } current)
        {
            return null;
        }

        SupabaseTablePolicy.ValidatePage(query.Offset, query.Limit);
        var plan = new QueryPlan(baseSql, current.SourceColumns, view.Filters, view.Sort, query.Offset, query.Limit);
        QueryPage page;
        try
        {
            page = await Provider.ExecutePlanAsync(plan, CancellationToken.None);
        }
        catch (SupabaseQueryException error)
        {
            throw new SupabaseTableSourceException($"Supabase refused the query behind table '{this.GetPrimaryKeyString()}': {error.Message}");
        }
        catch (SupabaseUnavailableException error)
        {
            throw new SupabaseTableSourceException(error.Message);
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
    public Task<SupabaseTableSummary?> ReadSummary()
        => Task.FromResult(Current.View is { } view ? new SupabaseTableSummary(view.Id, view.Title, view.Revision) : null);

    private static int Clamp(long count) => count > int.MaxValue ? int.MaxValue : (int)count;
}