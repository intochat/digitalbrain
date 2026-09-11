namespace DigitalBrain.Kernel;

internal sealed class NeuronNameFilter(params string[] routeValueKeys) : IEndpointFilter
{
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        foreach (var key in routeValueKeys)
        {
            if (!context.HttpContext.Request.RouteValues.ContainsKey(key))
            {
                var pattern = (context.HttpContext.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText;
                throw new InvalidOperationException($"NeuronNameFilter route value '{key}' is missing from route '{pattern}'.");
            }

            if (!EdgeResults.IsNeuronName(context.HttpContext.Request.RouteValues[key] as string))
            {
                return ValueTask.FromResult<object?>(Results.BadRequest());
            }
        }

        return next(context);
    }
}
