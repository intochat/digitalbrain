using System.Text.Json;
using DigitalBrain;
using DigitalBrain.Contracts;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Kernel.Enforcement;

namespace DigitalBrain.Postgres;

[PlatformOnly]
internal interface IPostgresTableResource : INeuron
{
    Task<PostgresAppTableResource> DescribeResource(string owner);
}

[GenerateSerializer]
internal sealed record PostgresAppTableResource(
    [property: Id(0)] string AppId,
    [property: Id(1)] string TableId,
    [property: Id(2)] string Origin,
    [property: Id(3)] PostgresTableDefinition Table);

internal sealed partial class PostgresTableNeuron : IPostgresTableResource
{
    public async Task<PostgresAppTableResource> DescribeResource(string owner)
    {
        var caller = CallerContextStamper.Require();
        var scope = JsonSerializer.Deserialize<string?[]>(owner);
        if (!CallerContextStamper.IsTrusted(caller) || caller.Kind == CallerKind.App
            || scope is not { Length: 2 } || scope[0] != BrainScope.CurrentId()
            || scope[1] is null || state.State.Owner != owner)
        { throw new UnauthorizedAccessException("This table belongs to another brain or app."); }
        await Tables(owner).RequireOpen();
        var table = state.State.Accepted ?? throw new PostgresQueryException("This table has not finished being defined.");
        return new(scope[1]!, this.GetPrimaryKeyString(), PostgresCapacityKind.OriginOrPlatform(state.State.Origin), table);
    }
}
