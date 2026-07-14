using System.Reflection;
using DigitalBrain.Kernel.Capabilities;
using DigitalBrain.Kernel.Contracts.Runtime;
using DigitalBrain.Kernel.Runtime;
using Orleans;

namespace DigitalBrain.Tests.Runtime;

public sealed class InoEffectExecutorTests
{
    private const string ActorScope = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public void Authorizes_only_a_signed_plan_for_an_explicit_effect_handler()
    {
        var handler = new RecordingHandler("salesforce.record.update");
        var authority = new InoEffectPlanAuthority(new TestRuntimeStateKeyRing());
        var planId = new string('b', 64);
        const string safePreview = "update the approved field";
        var scope = authority.Issue(planId, ActorScope, handler.ToolId, safePreview);
        var executor = new InoEffectExecutor(GrainFactory(), authority, [handler]);

        var authorized = executor.TryAuthorizeMutation(
            new InoToolRequest(handler.ToolId, InoToolAccess.Mutation, scope, safePreview),
            ActorScope,
            out var tool);
        var wrongTool = executor.TryAuthorizeMutation(
            new InoToolRequest("gmail.send", InoToolAccess.Mutation, scope, safePreview),
            ActorScope,
            out _);

        Assert.True(authorized);
        Assert.Equal(handler.ToolId, tool.ToolId);
        Assert.False(wrongTool);
    }

    [Fact]
    public void Duplicate_effect_handler_ids_fail_composition()
    {
        var authority = new InoEffectPlanAuthority(new TestRuntimeStateKeyRing());

        Assert.Throws<InvalidOperationException>(() => new InoEffectExecutor(
            GrainFactory(),
            authority,
            [new RecordingHandler("gmail.send"), new RecordingHandler("gmail.send")]));
    }

    private static IGrainFactory GrainFactory() => DispatchProxy.Create<IGrainFactory, ThrowingProxy>();

    private sealed class RecordingHandler(string toolId) : IInoEffectHandler
    {
        public string ToolId { get; } = toolId;

        public Task<InoToolEffectResult> ApplyAsync(
            string actorScope,
            byte[] payloadUtf8,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private class ThrowingProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => throw new NotSupportedException();
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
