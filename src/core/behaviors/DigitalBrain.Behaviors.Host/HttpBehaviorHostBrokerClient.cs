using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Security;
using DigitalBrain.Tasks;

namespace DigitalBrain.Behaviors;

internal sealed class HttpBehaviorHostBrokerClient : IBehaviorHostBrokerClient
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private readonly HttpClient httpClient;
    private readonly OwnerId owner;
    private readonly NeuronId task;
    private readonly AttemptId attempt;

    public HttpBehaviorHostBrokerClient(
        HttpClient httpClient,
        OwnerId owner,
        NeuronId task,
        AttemptId attempt)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        this.httpClient = httpClient;
        this.owner = owner;
        this.task = task;
        this.attempt = attempt;
    }

    public async ValueTask<ProtectedPayloadReference> StorePayloadAsync(
        OwnerId owner,
        NeuronId task,
        AttemptId attempt,
        ReadOnlyMemory<byte> plaintext,
        CancellationToken cancellationToken)
    {
        RequireBoundIdentity(owner, task, attempt);

        var response = await PostAsync(
            "v1/behaviors/broker/payloads/store",
            new StorePayloadRequestDto
            {
                Owner = this.owner.Value,
                TaskType = this.task.Type,
                TaskOwner = this.task.Owner.Value,
                TaskName = this.task.Name,
                Attempt = FormatGuid(this.attempt.Value),
                ContentBase64 = Convert.ToBase64String(plaintext.Span)
            },
            cancellationToken).ConfigureAwait(false);

        return await ReadProtectedReferenceAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<ReadOnlyMemory<byte>> LoadPayloadAsync(
        OwnerId owner,
        NeuronId task,
        AttemptId attempt,
        ProtectedPayloadReference reference,
        CancellationToken cancellationToken)
    {
        RequireBoundIdentity(owner, task, attempt);

        var response = await PostAsync(
            "v1/behaviors/broker/payloads/load",
            new LoadPayloadRequestDto
            {
                Owner = this.owner.Value,
                TaskType = this.task.Type,
                TaskOwner = this.task.Owner.Value,
                TaskName = this.task.Name,
                Attempt = FormatGuid(this.attempt.Value),
                Reference = ToWire(reference)
            },
            cancellationToken).ConfigureAwait(false);

        return await ReadPayloadContentAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<ReadOnlyMemory<byte>> LoadTriggerAsync(
        OwnerId owner,
        NeuronId task,
        BehaviorId behavior,
        BehaviorRevisionId revision,
        string caseId,
        ProtectedPayloadReference reference,
        CancellationToken cancellationToken)
    {
        if (owner != this.owner || task != this.task)
        {
            throw new BehaviorHostException("broker-identity-mismatch");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(caseId);

        var response = await PostAsync(
            "v1/behaviors/broker/triggers/load",
            new LoadTriggerRequestDto
            {
                Owner = this.owner.Value,
                TaskType = this.task.Type,
                TaskOwner = this.task.Owner.Value,
                TaskName = this.task.Name,
                Behavior = behavior.Value,
                Revision = revision.Value,
                CaseId = caseId,
                Reference = ToWire(reference)
            },
            cancellationToken).ConfigureAwait(false);

        return await ReadPayloadContentAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ReadOnlyMemory<byte>> ReadPayloadContentAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        using (response)
        {
            var body = await ReadRequiredJsonAsync<LoadPayloadResponseDto>(response, cancellationToken)
                .ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(body.ContentBase64))
            {
                throw new BehaviorHostException("invalid-payload-content");
            }

            try
            {
                return Convert.FromBase64String(body.ContentBase64);
            }
            catch (FormatException exception)
            {
                throw new BehaviorHostException("invalid-payload-content", exception);
            }
        }
    }

    public async ValueTask<TaskOperationSnapshot> PrepareAsync(
        PrepareTaskOperation command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        RequireBoundAttempt(command.Attempt);

        var response = await PostAsync(
            "v1/behaviors/broker/operations/prepare",
            new PrepareOperationRequestDto
            {
                Owner = owner.Value,
                TaskType = task.Type,
                TaskOwner = task.Owner.Value,
                TaskName = task.Name,
                Attempt = FormatGuid(this.attempt.Value),
                Sequence = command.Sequence,
                Edge = ToWire(command.Edge),
                RequestPayload = ToWire(command.RequestPayload)
            },
            cancellationToken).ConfigureAwait(false);

        return await ReadSnapshotAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<ReadTaskOperationResult> ReadAsync(
        ReadTaskOperation command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        RequireBoundAttempt(command.Attempt);

        var response = await PostAsync(
            "v1/behaviors/broker/operations/read",
            new ReadOperationRequestDto
            {
                Owner = owner.Value,
                TaskType = task.Type,
                TaskOwner = task.Owner.Value,
                TaskName = task.Name,
                Attempt = FormatGuid(this.attempt.Value),
                Sequence = command.Sequence
            },
            cancellationToken).ConfigureAwait(false);

        using (response)
        {
            var body = await ReadRequiredJsonAsync<ReadOperationResponseDto>(response, cancellationToken)
                .ConfigureAwait(false);
            if (body.Operation is null)
            {
                return new ReadTaskOperationResult(null);
            }

            return new ReadTaskOperationResult(FromWire(body.Operation));
        }
    }

    public async ValueTask<TaskOperationSnapshot> TransitionAsync(
        TransitionTaskOperation command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        RequireBoundAttempt(command.Attempt);

        var response = await PostAsync(
            "v1/behaviors/broker/operations/transition",
            new TransitionOperationRequestDto
            {
                Owner = owner.Value,
                TaskType = task.Type,
                TaskOwner = task.Owner.Value,
                TaskName = task.Name,
                Attempt = FormatGuid(this.attempt.Value),
                Sequence = command.Sequence,
                ExpectedPhase = (int)command.ExpectedPhase,
                Phase = (int)command.Phase,
                ResponsePayload = command.ResponsePayload is { } responsePayload
                    ? ToWire(responsePayload)
                    : null,
                RedactedSummary = command.RedactedSummary
            },
            cancellationToken).ConfigureAwait(false);

        return await ReadSnapshotAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<ProtectedPayloadReference> DispatchAsync(
        BehaviorCapabilityEdge edge,
        ProtectedPayloadReference requestPayload,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(edge);

        var response = await PostAsync(
            "v1/behaviors/broker/dispatch",
            new DispatchRequestDto
            {
                Owner = owner.Value,
                TaskType = task.Type,
                TaskOwner = task.Owner.Value,
                TaskName = task.Name,
                Attempt = FormatGuid(this.attempt.Value),
                Edge = ToWire(edge),
                RequestPayload = ToWire(requestPayload)
            },
            cancellationToken).ConfigureAwait(false);

        return await ReadProtectedReferenceAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private void RequireBoundIdentity(OwnerId owner, NeuronId task, AttemptId attempt)
    {
        if (owner != this.owner || task != this.task || attempt != this.attempt)
        {
            throw new BehaviorHostException("broker-identity-mismatch");
        }
    }

    private void RequireBoundAttempt(AttemptId attempt)
    {
        if (attempt != this.attempt)
        {
            throw new BehaviorHostException("broker-identity-mismatch");
        }
    }

    private async Task<HttpResponseMessage> PostAsync<TRequest>(
        string relativePath,
        TRequest body,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await httpClient
                .PostAsJsonAsync(relativePath, body, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new BehaviorHostException("broker-http-failed", exception);
        }

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        await ThrowForNonSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return response;
    }

    private static async Task ThrowForNonSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var statusCode = (int)response.StatusCode;
        string reason;
        try
        {
            reason = (await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)).Trim();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            response.Dispose();
            throw new BehaviorHostException($"http-{statusCode}", exception);
        }

        response.Dispose();
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new BehaviorHostException($"http-{statusCode}");
        }

        throw new BehaviorHostException(reason);
    }

    private static async Task<ProtectedPayloadReference> ReadProtectedReferenceAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        using (response)
        {
            var body = await ReadRequiredJsonAsync<ProtectedReferenceDto>(response, cancellationToken)
                .ConfigureAwait(false);
            return FromWire(body);
        }
    }

    private async Task<TaskOperationSnapshot> ReadSnapshotAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        using (response)
        {
            var body = await ReadRequiredJsonAsync<SnapshotDto>(response, cancellationToken)
                .ConfigureAwait(false);
            return FromWire(body);
        }
    }

    private static async Task<T> ReadRequiredJsonAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        if (stream.CanSeek && stream.Length == 0)
        {
            throw new BehaviorHostException("empty-broker-response");
        }

        T? body;
        try
        {
            body = await JsonSerializer
                .DeserializeAsync<T>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            throw new BehaviorHostException("invalid-broker-response", exception);
        }

        if (body is null)
        {
            throw new BehaviorHostException("empty-broker-response");
        }

        return body;
    }

    private TaskOperationSnapshot FromWire(SnapshotDto dto)
    {
        var responseAttempt = ParseAttempt(dto.Attempt);
        if (responseAttempt != attempt)
        {
            throw new BehaviorHostException("broker-attempt-mismatch");
        }

        if (dto.Edge is null || dto.RequestPayload is null)
        {
            throw new BehaviorHostException("invalid-broker-snapshot");
        }

        if (!Enum.IsDefined(typeof(TaskOperationPhase), dto.Phase))
        {
            throw new BehaviorHostException("invalid-broker-phase");
        }

        return new TaskOperationSnapshot(
            responseAttempt,
            dto.Sequence,
            FromWire(dto.Edge),
            FromWire(dto.RequestPayload),
            (TaskOperationPhase)dto.Phase,
            dto.ResponsePayload is null ? null : FromWire(dto.ResponsePayload),
            dto.RedactedSummary);
    }

    private static ProtectedPayloadReference FromWire(ProtectedReferenceDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Id))
        {
            throw new BehaviorHostException("invalid-protected-reference");
        }

        if (!Guid.TryParseExact(dto.Id, "N", out var id) || id == Guid.Empty)
        {
            throw new BehaviorHostException("invalid-protected-reference");
        }

        return new ProtectedPayloadReference(id, dto.ExpiresAt);
    }

    private static TaskOperationEdge FromWire(EdgeDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.TargetType)
            || string.IsNullOrWhiteSpace(dto.TargetOwner)
            || string.IsNullOrWhiteSpace(dto.TargetName)
            || string.IsNullOrWhiteSpace(dto.RequestId)
            || string.IsNullOrWhiteSpace(dto.ResponseId))
        {
            throw new BehaviorHostException("invalid-operation-edge");
        }

        var target = new NeuronId(dto.TargetType, new OwnerId(dto.TargetOwner), dto.TargetName);
        return new TaskOperationEdge(
            target,
            dto.RequestId,
            dto.RequestVersion,
            dto.ResponseId,
            dto.ResponseVersion);
    }

    private static AttemptId ParseAttempt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !Guid.TryParseExact(value, "N", out var id)
            || id == Guid.Empty)
        {
            throw new BehaviorHostException("invalid-attempt");
        }

        return new AttemptId(id);
    }

    private static ProtectedReferenceDto ToWire(ProtectedPayloadReference reference)
        => new()
        {
            Id = FormatGuid(reference.Id),
            ExpiresAt = reference.ExpiresAt
        };

    private static EdgeDto ToWire(TaskOperationEdge edge)
        => new()
        {
            TargetType = edge.Target.Type,
            TargetOwner = edge.Target.Owner.Value,
            TargetName = edge.Target.Name,
            RequestId = edge.RequestSynapseId,
            RequestVersion = edge.RequestSchemaVersion,
            ResponseId = edge.ResponseSynapseId,
            ResponseVersion = edge.ResponseSchemaVersion
        };

    private static EdgeDto ToWire(BehaviorCapabilityEdge edge)
        => new()
        {
            TargetType = edge.Target.Type,
            TargetOwner = edge.Target.Owner.Value,
            TargetName = edge.Target.Name,
            RequestId = edge.RequestSynapseId,
            RequestVersion = edge.RequestSchemaVersion,
            ResponseId = edge.ResponseSynapseId,
            ResponseVersion = edge.ResponseSchemaVersion
        };

    private static string FormatGuid(Guid value) => value.ToString("N");

    private static JsonSerializerOptions CreateJsonOptions()
        => new(JsonSerializerDefaults.Web);

    private sealed class StorePayloadRequestDto
    {
        public string Owner { get; set; } = "";
        public string TaskType { get; set; } = "";
        public string TaskOwner { get; set; } = "";
        public string TaskName { get; set; } = "";
        public string Attempt { get; set; } = "";
        public string ContentBase64 { get; set; } = "";
    }

    private sealed class LoadPayloadRequestDto
    {
        public string Owner { get; set; } = "";
        public string TaskType { get; set; } = "";
        public string TaskOwner { get; set; } = "";
        public string TaskName { get; set; } = "";
        public string Attempt { get; set; } = "";
        public ProtectedReferenceDto Reference { get; set; } = new();
    }

    private sealed class LoadTriggerRequestDto
    {
        public string Owner { get; set; } = "";
        public string TaskType { get; set; } = "";
        public string TaskOwner { get; set; } = "";
        public string TaskName { get; set; } = "";
        public string Behavior { get; set; } = "";
        public string Revision { get; set; } = "";
        public string CaseId { get; set; } = "";
        public ProtectedReferenceDto Reference { get; set; } = new();
    }

    private sealed class LoadPayloadResponseDto
    {
        public string? ContentBase64 { get; set; }
    }

    private sealed class PrepareOperationRequestDto
    {
        public string Owner { get; set; } = "";
        public string TaskType { get; set; } = "";
        public string TaskOwner { get; set; } = "";
        public string TaskName { get; set; } = "";
        public string Attempt { get; set; } = "";
        public int Sequence { get; set; }
        public EdgeDto Edge { get; set; } = new();
        public ProtectedReferenceDto RequestPayload { get; set; } = new();
    }

    private sealed class ReadOperationRequestDto
    {
        public string Owner { get; set; } = "";
        public string TaskType { get; set; } = "";
        public string TaskOwner { get; set; } = "";
        public string TaskName { get; set; } = "";
        public string Attempt { get; set; } = "";
        public int Sequence { get; set; }
    }

    private sealed class ReadOperationResponseDto
    {
        public SnapshotDto? Operation { get; set; }
    }

    private sealed class TransitionOperationRequestDto
    {
        public string Owner { get; set; } = "";
        public string TaskType { get; set; } = "";
        public string TaskOwner { get; set; } = "";
        public string TaskName { get; set; } = "";
        public string Attempt { get; set; } = "";
        public int Sequence { get; set; }
        public int ExpectedPhase { get; set; }
        public int Phase { get; set; }
        public ProtectedReferenceDto? ResponsePayload { get; set; }
        public string? RedactedSummary { get; set; }
    }

    private sealed class DispatchRequestDto
    {
        public string Owner { get; set; } = "";
        public string TaskType { get; set; } = "";
        public string TaskOwner { get; set; } = "";
        public string TaskName { get; set; } = "";
        public string Attempt { get; set; } = "";
        public EdgeDto Edge { get; set; } = new();
        public ProtectedReferenceDto RequestPayload { get; set; } = new();
    }

    private sealed class ProtectedReferenceDto
    {
        public string? Id { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
    }

    private sealed class EdgeDto
    {
        public string? TargetType { get; set; }
        public string? TargetOwner { get; set; }
        public string? TargetName { get; set; }
        public string? RequestId { get; set; }
        public int RequestVersion { get; set; }
        public string? ResponseId { get; set; }
        public int ResponseVersion { get; set; }
    }

    private sealed class SnapshotDto
    {
        public string? Attempt { get; set; }
        public int Sequence { get; set; }
        public EdgeDto? Edge { get; set; }
        public ProtectedReferenceDto? RequestPayload { get; set; }
        public int Phase { get; set; }
        public ProtectedReferenceDto? ResponsePayload { get; set; }
        public string? RedactedSummary { get; set; }
    }
}
