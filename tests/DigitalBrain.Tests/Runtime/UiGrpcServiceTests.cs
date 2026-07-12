extern alias McpProject;

using DigitalBrain.Core.Runtime;
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
}
