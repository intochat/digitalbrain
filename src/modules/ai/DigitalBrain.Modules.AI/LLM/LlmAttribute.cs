using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.AI;

[AttributeUsage(AttributeTargets.Parameter)]
internal sealed class LlmAttribute<TModel> : FromKeyedServicesAttribute
    where TModel : LLM
{
    public LlmAttribute() : base(typeof(TModel))
    {
    }
}
