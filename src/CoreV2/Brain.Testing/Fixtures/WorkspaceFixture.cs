using Brain.Abstractions.Context;
using Brain.Abstractions.Identity;

namespace Brain.Testing.Fixtures;

public sealed class WorkspaceFixture
{
    public WorkspaceContext Caller(string workspace, string principal)
        => new(new WorkspaceId(workspace), new PrincipalId(principal), isServicePrincipal: false);
}
