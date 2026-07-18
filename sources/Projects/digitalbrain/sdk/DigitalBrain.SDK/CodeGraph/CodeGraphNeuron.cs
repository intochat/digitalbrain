using System.Text.Json;
using DigitalBrain.Runtime;
using DigitalBrain.Runtime.Neurons;
using Orleans.Journaling;

namespace DigitalBrain.SDK.CodeGraph;

[ImplicitStreamSubscription(CodeGraphNeuronType)]
internal sealed class CodeGraphNeuron(
    [FromKeyedServices("incoming")] IDurableList<Synapse> incoming,
    [FromKeyedServices("outgoing")] IDurableList<Synapse> outgoing,
    IGrainFactory grains,
    ILogger<CodeGraphNeuron> logger)
    : Neuron(incoming, outgoing, grains, logger),
      ICodeGraphNeuron,
      INeuronMetadata,
      IHandle<CodeGraphQueryRequest>
{
    public const string CodeGraphNeuronType = nameof(CodeGraphNeuron);

    public static NeuronId Id => new("data/codegraph");
    public static string Icon => "code";
    public static NeuronCapability Capabilities => NeuronCapability.Storage | NeuronCapability.Fast;

    private readonly CodeGraphDatabaseContext _db = new();

    protected override async Task HandleSynapseAsync(Synapse synapse)
    {
        if (synapse is CodeGraphQueryRequest request)
        {
            await HandleQueryAsync(request);
        }
    }

    public async Task HandleAsync(CodeGraphQueryRequest request, CancellationToken cancellationToken)
    {
        await HandleQueryAsync(request);
    }

    private async Task HandleQueryAsync(CodeGraphQueryRequest request)
    {
        logger.LogInformation("Processing CodeGraph query of type {Type} with query '{Query}'", request.QueryType, request.QueryText);

        try
        {
            if (!File.Exists(_db.DatabasePath))
            {
                throw new FileNotFoundException($"CodeGraph SQLite database not found at {_db.DatabasePath}. Please run 'codegraph index' first.");
            }

            await using var connection = await _db.OpenConnectionAsync();
            await using var command = connection.CreateCommand();

            switch (request.QueryType)
            {
                case CodeGraphQueryType.Search:
                    command.CommandText = @"
                        SELECT id, kind, name, qualified_name, file_path, start_line, end_line, docstring, signature, visibility
                        FROM nodes
                        WHERE name LIKE @query OR qualified_name LIKE @query
                        LIMIT @limit;";
                    command.Parameters.AddWithValue("@query", $"%{request.QueryText}%");
                    break;

                case CodeGraphQueryType.Context:
                    command.CommandText = @"
                        SELECT id, kind, name, qualified_name, file_path, language, start_line, end_line, start_column, end_column, docstring, signature, visibility, is_exported, is_async, is_static, is_abstract, decorators, type_parameters
                        FROM nodes
                        WHERE id = @query OR qualified_name = @query OR name = @query;";
                    command.Parameters.AddWithValue("@query", request.QueryText);
                    break;

                case CodeGraphQueryType.Callers:
                    command.CommandText = @"
                        SELECT n.id, n.kind, n.name, n.qualified_name, n.file_path, n.start_line, e.line, e.col
                        FROM edges e
                        JOIN nodes n ON e.source = n.id
                        WHERE e.target = @query AND e.kind = 'calls'
                        LIMIT @limit;";
                    command.Parameters.AddWithValue("@query", request.QueryText);
                    break;

                case CodeGraphQueryType.Callees:
                    command.CommandText = @"
                        SELECT n.id, n.kind, n.name, n.qualified_name, n.file_path, n.start_line, e.line, e.col
                        FROM edges e
                        JOIN nodes n ON e.target = n.id
                        WHERE e.source = @query AND e.kind = 'calls'
                        LIMIT @limit;";
                    command.Parameters.AddWithValue("@query", request.QueryText);
                    break;

                case CodeGraphQueryType.Impact:
                    command.CommandText = @"
                        SELECT n.id, n.kind, n.name, n.qualified_name, n.file_path, e.kind
                        FROM edges e
                        JOIN nodes n ON e.source = n.id
                        WHERE e.target = @query
                        LIMIT @limit;";
                    command.Parameters.AddWithValue("@query", request.QueryText);
                    break;

                default:
                    throw new NotSupportedException($"Query type {request.QueryType} is not supported.");
            }

            command.Parameters.AddWithValue("@limit", request.Limit);

            await using var reader = await command.ExecuteReaderAsync();

            var columns = new List<string>();
            for (var i = 0; i < reader.FieldCount; i++)
            {
                columns.Add(reader.GetName(i));
            }

            var rows = new List<IReadOnlyList<string?>>();
            var structuredList = new List<Dictionary<string, object?>>();

            while (await reader.ReadAsync())
            {
                var row = new string?[reader.FieldCount];
                var structuredRow = new Dictionary<string, object?>();

                for (var i = 0; i < reader.FieldCount; i++)
                {
                    var isNull = reader.IsDBNull(i);
                    var val = isNull ? null : reader.GetValue(i);
                    row[i] = val?.ToString();
                    structuredRow[reader.GetName(i)] = val;
                }

                rows.Add(row);
                structuredList.Add(structuredRow);
            }

            var jsonResult = JsonSerializer.Serialize(structuredList, new JsonSerializerOptions { WriteIndented = true });

            await FireSynapseAsync(new CodeGraphQueryResponse(Success: true,
        ErrorMessage: null,
        Columns: columns,
        Rows: rows,
        ResultJson: jsonResult) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: request.CorrelationId,
            causationId: request.SynapseId,
            callerNeuronId: default,
            callerNeuronType: null,
            receiverNeuronId: request.CallerNeuronId,
            receiverNeuronType: request.CallerNeuronType ?? "External",
            timestamp: default
        ) });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to execute CodeGraph query");

            await FireSynapseAsync(new CodeGraphQueryResponse(Success: false,
        ErrorMessage: ex.Message,
        Columns: Array.Empty<string>(),
        Rows: Array.Empty<IReadOnlyList<string?>>(),
        ResultJson: null) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: request.CorrelationId,
            causationId: request.SynapseId,
            callerNeuronId: default,
            callerNeuronType: null,
            receiverNeuronId: request.CallerNeuronId,
            receiverNeuronType: request.CallerNeuronType ?? "External",
            timestamp: default
        ) });
        }
    }
}
