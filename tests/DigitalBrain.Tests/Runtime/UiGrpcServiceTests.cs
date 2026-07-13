extern alias McpProject;

using DigitalBrain.Core.Runtime;
using DigitalBrain.Kernel.Runtime;
using Grpc.Core;
using UiGrpcService = McpProject::DigitalBrain.Mcp.UiGrpcService;

namespace DigitalBrain.Tests.Runtime;

public sealed class UiGrpcServiceTests
{
    [Fact]
    public void Wrong_revision_is_mapped_to_failed_precondition()
    {
        Assert.Equal(
            StatusCode.FailedPrecondition,
            UiGrpcService.StatusForActionRejection(ActionRejection.WrongRevision));
    }

    [Fact]
    public void Stale_unavailable_action_is_mapped_to_failed_precondition()
    {
        Assert.Equal(
            StatusCode.FailedPrecondition,
            UiGrpcService.StatusForActionRejection(ActionRejection.Unavailable));
    }

    [Fact]
    public void Forged_action_is_mapped_to_permission_denied()
    {
        Assert.Equal(
            StatusCode.PermissionDenied,
            UiGrpcService.StatusForActionRejection(ActionRejection.Forged));
    }

    [Fact]
    public void Action_tokens_are_refreshed_only_when_the_binding_set_changes()
    {
        var issuedBinding = new SurfaceActionBinding(
            ConversationSurfacePayload.SendBindingId,
            ConversationSurfacePayload.HomeSurfaceId,
            1,
            ConversationSurfacePayload.SendActionType,
            ConversationSurfacePayload.SendInputSchema,
            "ui.action",
            UiProtocol.ActionSchemaVersion,
            new string('a', 64),
            1,
            0,
            DateTimeOffset.UtcNow.AddMinutes(5),
            null,
            null);

        Assert.False(UiGrpcService.ActionBindingsChanged([issuedBinding], [issuedBinding]));
        Assert.True(UiGrpcService.ActionBindingsChanged(
            [issuedBinding],
            [issuedBinding with { SurfaceRevision = 2, TokenHash = new string('b', 64) }]));
        Assert.True(UiGrpcService.ActionBindingsChanged([issuedBinding], []));
    }

}
