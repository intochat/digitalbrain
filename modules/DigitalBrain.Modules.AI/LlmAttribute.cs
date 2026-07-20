using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.AI;

[AttributeUsage(AttributeTargets.Parameter)]
public sealed class LlmAttribute<TModel> : FromKeyedServicesAttribute
    where TModel : LLM
{
    public LlmAttribute() : base(typeof(TModel))
    {
    }
}
