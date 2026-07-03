using Aspire.Hosting.ApplicationModel;

namespace DigitalBrain.Aspire;

public sealed class DigitalBrainResource(string name) : Resource(name), IResourceWithConnectionString
{
    public ReferenceExpression ConnectionStringExpression =>
        ReferenceExpression.Create($"digitalbrain://{Name}");

    public ValueTask<string?> GetConnectionStringAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<string?>(ConnectionStringExpression.ValueExpression);
}