using System.Security.Cryptography;
using DigitalBrain.Core.Runtime;
using DigitalBrain.Kernel.Runtime;

namespace DigitalBrain.Tests.Runtime;

public sealed class TypedInoOperationCapabilityTests
{
    private const string ActorScope = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public void Execution_evidence_binds_the_approved_payload_and_execution_identity()
    {
        var payload = "prepared-provider-operation"u8.ToArray();
        var evidence = new InoEffectExecution(
            ActorScope,
            "operation-1",
            "crm.record.update",
            "update the approved field",
            Convert.ToHexStringLower(SHA256.HashData(payload)),
            "effect-1",
            "idempotency-1",
            "execution-proof",
            payload);

        Assert.True(evidence.HasValidPayloadHash());
        Assert.Equal(ActorScope, evidence.ActorScope);
        Assert.Equal("operation-1", evidence.OperationId);
        Assert.Equal("crm.record.update", evidence.ToolId);
        Assert.Equal("update the approved field", evidence.SafePreview);
        Assert.Equal("idempotency-1", evidence.IdempotencyKey);
        Assert.Equal("execution-proof", evidence.ExecutionProof);
    }

    [Fact]
    public async Task Gateway_uses_the_composed_typed_capability_without_provider_dispatch()
    {
        var capability = new RecordingCapability();
        var authority = new InoEffectPlanAuthority(new TestRuntimeStateKeyRing());
        var actorScope = ActorScope;
        var planId = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        var safePreview = "update the approved field";
        var scope = authority.Issue(planId, actorScope, capability.ToolId, safePreview);
        var gateway = new PlanInoToolGateway(null!, authority, capability);

        var authorized = gateway.TryAuthorizeMutation(
            new InoToolRequest(capability.ToolId, InoToolAccess.Mutation, scope, safePreview),
            actorScope,
            out var tool);

        Assert.True(authorized);
        Assert.Equal(capability.ToolId, tool.ToolId);
        Assert.Equal(1, capability.SupportCalls);
        await Task.CompletedTask;
    }

    private sealed class RecordingCapability : IInoOperationCapability
    {
        public string ToolId => "crm.record.update";
        public int SupportCalls { get; private set; }

        public bool Supports(string toolId)
        {
            SupportCalls++;
            return string.Equals(toolId, ToolId, StringComparison.Ordinal);
        }

        public Task<InoReadOperationResult> ReadAsync(
            InoReadOperation request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<InoMutationPreviewResult> PreviewAsync(
            InoMutationPreview request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<InoEffectApplyResult> ApplyAsync(
            InoEffectExecution request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<InoEffectVerificationResult> VerifyAsync(
            InoEffectExecution request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class TestRuntimeStateKeyRing : IRuntimeStateKeyRing
    {
        private readonly ReadOnlyMemory<byte> _encryptionKey = Enumerable.Repeat((byte)17, 32).ToArray();
        public int ActiveKekVersion => 1;
        public ReadOnlyMemory<byte> SigningKey { get; } = Enumerable.Repeat((byte)29, 32).ToArray();
        public bool TryGetKek(int version, out ReadOnlyMemory<byte> key)
        {
            key = version == ActiveKekVersion ? _encryptionKey : default;
            return version == ActiveKekVersion;
        }
    }
}
