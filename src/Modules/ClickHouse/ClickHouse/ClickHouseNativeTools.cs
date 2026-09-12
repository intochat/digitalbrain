using System.ComponentModel;
using System.Text.Json;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Descriptors;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Chat;
using DigitalBrain.UI;
using Microsoft.Extensions.AI;

namespace DigitalBrain.ClickHouse;

// The agent's three doors into ClickHouse. Refinement, paging and charts reuse the existing
// read_table, update_table_view, list_tables and render_chart tools on the table this creates.
internal sealed class ClickHouseNativeTools(IGrainFactory grains, INeuronInvoker invoker, TableService tables)
{
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web);

    internal AIFunction CreateSchema()
    {
        Task<JsonElement> Invoke(
            [Description("Table name; omit to read the index of every table in the database.")] string? table = null,
            CancellationToken cancellationToken = default)
            => ResultAsync(() => ClickHouse.ReadSchema(new ReadClickHouseSchema(table), cancellationToken), "clickhouse_schema");

        return AIFunctionFactory.Create(Invoke, new AIFunctionFactoryOptions
        {
            Name = "clickhouse_schema",
            Description = "Read the ClickHouse database schema (tables, engines, columns, types, and sample values of categorical "
                + "columns). Start with no table for the index. Always read the schema before writing SQL; column names must "
                + "match exactly and filters on categorical columns must use the sample values as spelled.",
        });
    }

    internal AIFunction CreateQuery()
    {
        Task<JsonElement> Invoke(
            [Description("One read-only ClickHouse SELECT (or WITH … SELECT) without FORMAT or SETTINGS")] string sql,
            [Description("Row cap, 1–1000")] int maxRows = ClickHouseQuery.DefaultMaxRows,
            CancellationToken cancellationToken = default)
            => ResultAsync(() => ClickHouse.Query(new ClickHouseQuery(sql, maxRows), cancellationToken), "clickhouse_query");

        return AIFunctionFactory.Create(Invoke, new AIFunctionFactoryOptions
        {
            Name = "clickhouse_query",
            Description = "Run one read-only ClickHouse SELECT and return typed rows (max 1000). Use ClickHouse SQL "
                + "(count(), groupArray, has(tags,'x'), hasAny, positionCaseInsensitiveUTF8, LIMIT). For results the person "
                + "should see or refine, call show_query_table instead of pasting rows. On failure the server's error text "
                + "is returned so the query can be corrected.",
        });
    }

    internal AIFunction CreateShowQueryTable()
    {
        Task<JsonElement> Invoke(
            [Description("Short table title")] string title,
            [Description("The read-only SELECT whose rows the table shows live; alias every column with a unique name")] string sql,
            [Description("Only when the conversation context has a 'Chat:' line: that uichat name, exactly as stated. Otherwise omit it.")] string? chatName = null,
            CancellationToken cancellationToken = default)
            => ShowQueryTableAsync(chatName, title, sql, cancellationToken);

        return AIFunctionFactory.Create(Invoke, new AIFunctionFactoryOptions
        {
            Name = "show_query_table",
            Description = "Show the rows of a ClickHouse SELECT as a live, pageable table the person can filter and sort. "
                + "The table keeps the query; refine it later with update_table_view (filters are AND-combined) and page it "
                + "with read_table. Use it whenever the person wants to see, filter or work with query results.",
        });
    }

    private IClickHouse ClickHouse => grains.GetGrain<IClickHouse>(new NeuronId(ClickHouseNames.NeuronType, ClickHouseNames.DefaultNeuron).ToGrainId());

    private async Task<JsonElement> ShowQueryTableAsync(string? chatName, string title, string sql, CancellationToken cancellationToken)
    {
        NeuronId? chat = null;
        if (!string.IsNullOrWhiteSpace(chatName))
        {
            if (!NeuronId.TryParse(chatName, out var parsed) || parsed.Type != UIVocabulary.ChatType)
            {
                return Error("invalid_chat", UiTools.InvalidChat);
            }

            chat = parsed;
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            return Error("invalid_title", "title must not be blank.");
        }

        try
        {
            ClickHouseQueryGuard.Validate(sql);
        }
        catch (ArgumentException error)
        {
            return Error("query_refused", error.Message);
        }

        try
        {
            var name = $"{ClickHouseNames.TableIdPrefix}{Guid.NewGuid():N}";
            var neuron = new NeuronId(ClickHouseNames.TableType, name);
            var command = new CreateQueryTableCommand(CommandId.New(), new CreateQueryTable(title.Trim(), sql));

            // Connect first: the reaction fires the card signal and needs a chat listener. Without a chat
            // the table still exists and is listed; the workspace opens it from the tool result.
            if (chat is { } listener)
            {
                await grains.GetGrain<INeuron>(neuron.ToGrainId()).Connect(listener, UIVocabulary.TableRendered).ConfigureAwait(false);
            }

            await invoker.InvokeAsync(neuron, "clickhouse.table", "create-query",
                JsonSerializer.SerializeToElement(command, ClickHouseJson.Default.CreateQueryTableCommand), cancellationToken).ConfigureAwait(false);
            try
            {
                await TableService.WaitAppliedAsync(grains.GetGrain<IClickHouseTable>(neuron.ToGrainId()), name, command.Id, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // Still pending, so it may apply later: list it now rather than orphan it. A refused
                // query throws TableValidationException instead and never reaches the catalog.
                await tables.RegisterAsync(neuron, cancellationToken).ConfigureAwait(false);
                throw;
            }

            await tables.RegisterAsync(neuron, cancellationToken).ConfigureAwait(false);
            var carded = chat is { } target && await WaitForCardAsync(target, name, cancellationToken).ConfigureAwait(false);

            var snapshot = await tables.ReadAsync(name, cancellationToken: cancellationToken).ConfigureAwait(false);
            return JsonSerializer.SerializeToElement(new
            {
                kind = snapshot.Kind,
                snapshot.Id,
                snapshot.Title,
                snapshot.Revision,
                snapshot.Columns,
                snapshot.Rows,
                snapshot.Filters,
                snapshot.Sort,
                snapshot.VisibleColumns,
                snapshot.TotalRows,
                snapshot.FilteredRows,
                snapshot.Offset,
                snapshot.Limit,
                message = (carded
                    ? $"Table '{snapshot.Title}' is now showing in the chat as card '{name}' (id {name}). "
                    : $"Table '{snapshot.Title}' is saved as {name}; the workspace opens it from this result. ")
                    + "Refine it with update_table_view (filters are AND-combined); read a page with read_table.",
            }, WireJson);
        }
        catch (TableValidationException error)
        {
            return Error("query_refused", error.Message);
        }
        catch (TableRevisionConflictException error)
        {
            return Error("failed", error.Message);
        }
        catch (TableNotFoundException error)
        {
            return Error("failed", error.Message);
        }
        catch (TableSourceException error)
        {
            return Error("source_failed", error.Message);
        }
        catch (TimeoutException error)
        {
            return Error("pending", error.Message + " List tables before retrying; do not claim success.");
        }
        catch (Exception error) when (error is not OperationCanceledException && !TransientFailure.Covers(error))
        {
            return Error("failed", $"show_query_table failed: {error.GetType().Name}: {error.Message}");
        }
    }

    // The command acknowledges admission, not completion; a uichat turn must not settle before the
    // chat holds the card. A chat with no running turn (the workspace agent names none) gets no card,
    // and the table is already created and listed, so waiting is best effort and never fails the tool.
    private async Task<bool> WaitForCardAsync(NeuronId chat, string name, CancellationToken cancellationToken)
    {
        var target = grains.GetGrain<IChat>(chat.ToGrainId());
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            while (true)
            {
                var turns = await target.ReadTurns(new ReadTurns()).WaitAsync(deadline.Token).ConfigureAwait(false);
                if (turns.Turns.Any(turn => turn.Cards?.Any(card => card.Kind == UiCardKinds.Table && card.Name == name) == true))
                {
                    return true;
                }

                if (!turns.Turns.Any(turn => turn.Status == ChatTurnStatus.Running))
                {
                    return false;
                }

                await Task.Delay(25, deadline.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    // A slow query is an answer for the model, not a transient fault to retry the whole turn on.
    private static async Task<JsonElement> ResultAsync<T>(Func<Task<T>> read, string tool)
    {
        try
        {
            return JsonSerializer.SerializeToElement(await read().ConfigureAwait(false), WireJson);
        }
        catch (ClickHouseQueryException error)
        {
            return Error("query_failed", error.Message);
        }
        catch (ClickHouseUnavailableException error)
        {
            return Error("unavailable", error.Message);
        }
        catch (TimeoutException error)
        {
            return Error("timeout", $"{tool} did not finish in time: {error.Message} Narrow the query with WHERE or LIMIT and try again.");
        }
        catch (Exception error) when (error is not OperationCanceledException && !TransientFailure.Covers(error))
        {
            return Error("failed", $"{tool} failed: {error.GetType().Name}: {error.Message}");
        }
    }

    private static JsonElement Error(string code, string message)
        => JsonSerializer.SerializeToElement(new { kind = "clickhouseError", code, message }, WireJson);
}
