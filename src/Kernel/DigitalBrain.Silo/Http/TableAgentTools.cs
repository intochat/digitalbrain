using System.ComponentModel;
using System.Text.Json;
using DigitalBrain.UI;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Kernel;

/// <summary>Direct table tools share the same application service as the ui HTTP controls.</summary>
internal sealed class TableAgentTools(TableService tables)
{
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web);

    public IReadOnlyList<AITool> Create() =>
    [
        AIFunctionFactory.Create(CreateTableAsync, new AIFunctionFactoryOptions
        {
            Name = "create_table",
            Description = "Create a persistent interactive UI table. Supply title, typed columns (id,label,type: text/number/date/boolean), and rows (unique id,cells in column order as native JSON scalar values or null). Limits: 1000 rows, 32 columns; numbers up to 15 significant decimal digits and absolute value 9007199254740991. Use text for larger exact identifiers. Returns the saved table ID, schema, view state and first 50 rows. Use for generated datasets rather than Markdown tables.",
        }),
        AIFunctionFactory.Create(ReadTableAsync, new AIFunctionFactoryOptions
        {
            Name = "read_table",
            Description = "Read the authoritative current table schema, filters, sort, revision, counts and a page of matching rows. Always read before answering table-dependent questions or changing filters: UI actions may have changed the state since the last tool result. Page limit is 1..200; default 50.",
        }),
        AIFunctionFactory.Create(UpdateViewAsync, new AIFunctionFactoryOptions
        {
            Name = "update_table_view",
            Description = "Replace the saved table view with expectedRevision, filters, sort and visibleColumns. Read first and preserve filters for additive requests. Filters are AND-combined with columnId, operator (eq,neq,contains,gt,gte,lt,lte,isNull,isNotNull), and typed JSON value. Sort is null or {columnId,descending}. visibleColumns lists column IDs. Empty filters clears filtering without deleting rows. A conflict means read fresh state and reconcile; never claim failed changes succeeded.",
        }),
        AIFunctionFactory.Create(ListTablesAsync, new AIFunctionFactoryOptions
        {
            Name = "list_tables",
            Description = "Find saved UI tables by their ID, title and current revision, including tables created in previous conversations. Use read_table to inspect one.",
        }),
    ];

    private async Task<JsonElement> CreateTableAsync(
        [Description("The table title, columns, and rows. Cells use native JSON numbers/booleans, ISO date strings, text, or null.")] CreateTable table,
        CancellationToken cancellationToken = default)
        => await ResultAsync(() => tables.CreateAsync(table, cancellationToken));

    private async Task<JsonElement> ReadTableAsync(
        [Description("The table ID returned by create_table or list_tables.")] string id,
        int offset = 0, int limit = 50, CancellationToken cancellationToken = default)
        => await ResultAsync(() => tables.ReadAsync(id, offset, limit, cancellationToken));

    private async Task<JsonElement> UpdateViewAsync(
        [Description("The table ID.")] string id,
        [Description("Complete replacement view, including expectedRevision from the latest read, filters, sort and visibleColumns.")] UpdateTableView view,
        CancellationToken cancellationToken = default)
        => await ResultAsync(() => tables.UpdateAsync(id, view, cancellationToken));

    private async Task<JsonElement> ListTablesAsync(CancellationToken cancellationToken = default)
        => await ResultAsync(() => tables.ListAsync(cancellationToken));

    private static async Task<JsonElement> ResultAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return JsonSerializer.SerializeToElement(await action(), WireJson);
        }
        catch (TableRevisionConflictException error)
        {
            return Error("revision_conflict", error.Message + " Read the table again before reconciling your change.");
        }
        catch (TableNotFoundException error)
        {
            return Error("not_found", error.Message);
        }
        catch (TableValidationException error)
        {
            return Error("invalid_table", error.Message);
        }
        catch (TimeoutException)
        {
            return Error("pending", "The operation has not completed. List and read saved tables before retrying; do not claim success.");
        }
    }

    private static JsonElement Error(string code, string message)
        => JsonSerializer.SerializeToElement(new { kind = "tableError", code, message }, WireJson);
}
