using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.AI;

[AttributeUsage(AttributeTargets.Parameter)]
internal sealed class LlmAttribute<TModel>() : FromKeyedServicesAttribute(typeof(TModel))
    where TModel : ILLM;
